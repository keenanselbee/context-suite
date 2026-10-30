use std::collections::{HashMap, HashSet};
use std::env;
use std::fs;
use std::io::{self, Cursor, Read, Write};
use std::path::PathBuf;
use std::sync::atomic::{AtomicBool, Ordering};
use std::sync::{Arc, Mutex};

use base64::Engine;
use resvg::{tiny_skia, usvg};

const MAX_INPUT: usize = 1_048_576;
const MAX_ELEMENTS: usize = 2_000;
const MAX_DEPTH: usize = 64;
const MAX_EMBEDDED: usize = 8;
const MAX_EMBEDDED_BYTES: usize = 384 * 1024;
const MAX_EMBEDDED_PIXELS: u64 = 1_000_000;
const MAX_TOTAL_EMBEDDED_PIXELS: u64 = 2_000_000;
const SVG_NS: &str = "http://www.w3.org/2000/svg";
const XLINK_NS: &str = "http://www.w3.org/1999/xlink";
const XML_NS: &str = "http://www.w3.org/XML/1998/namespace";

static RENDER_WARNING: AtomicBool = AtomicBool::new(false);
static WARNING_LOGGER: WarningLogger = WarningLogger;

struct WarningLogger;

impl log::Log for WarningLogger {
    fn enabled(&self, metadata: &log::Metadata<'_>) -> bool { metadata.level() <= log::Level::Warn }
    fn log(&self, record: &log::Record<'_>) {
        if self.enabled(record.metadata()) { RENDER_WARNING.store(true, Ordering::Relaxed); }
    }
    fn flush(&self) {}
}

#[derive(Debug)]
struct Failure { code: i32, message: &'static str }

fn invalid(message: &'static str) -> Failure { Failure { code: 2, message } }
fn unsupported(message: &'static str) -> Failure { Failure { code: 3, message } }
fn resource(message: &'static str) -> Failure { Failure { code: 4, message } }
fn engine(message: &'static str) -> Failure { Failure { code: 6, message } }

#[derive(Clone, Copy)]
struct Limits { side: u32, pixels: u64 }

#[derive(Default)]
struct Admission { images: usize, direct_images: usize, image_pixels: u64, filtered: bool, text: bool, uses: usize }

fn css_declarations(value: &str) -> Result<(), Failure> {
    if value.len() > 32_768 || value.contains("/*") || value.contains('}') || value.contains('{') || value.contains('@') {
        return Err(unsupported("unsupported-css-syntax"));
    }
    for item in value.split(';').map(str::trim).filter(|s| !s.is_empty()) {
        let (name, value) = item.split_once(':').ok_or_else(|| invalid("invalid-css-declaration"))?;
        let name = name.trim();
        let value = value.trim();
        if value.is_empty() || value.contains("!important") || value.contains("expression(") || value.contains("var(") || value.contains("calc(") || value.contains("@") {
            return Err(unsupported("unsupported-css-value"));
        }
        if !style_property(name) { return Err(unsupported("unsupported-css-property")); }
        safe_value(value)?;
    }
    Ok(())
}

fn style_property(name: &str) -> bool {
    matches!(name,
        "fill" | "fill-opacity" | "fill-rule" | "stroke" | "stroke-width" | "stroke-opacity" |
        "stroke-linecap" | "stroke-linejoin" | "stroke-miterlimit" | "stroke-dasharray" |
        "stroke-dashoffset" | "opacity" | "display" | "visibility" | "color" | "clip-path" |
        "clip-rule" | "mask" | "filter" | "stop-color" | "stop-opacity" |
        "font-family" | "font-size" | "font-style" | "font-weight" | "font-stretch" |
        "text-anchor" | "letter-spacing" | "word-spacing" | "paint-order" |
        "shape-rendering" | "text-rendering" | "image-rendering" | "dominant-baseline" |
        "baseline-shift" | "text-decoration" | "vector-effect")
}

fn safe_value(value: &str) -> Result<(), Failure> {
    let lower = value.to_ascii_lowercase();
    if lower.contains("://") || lower.contains("file:") || lower.contains("data:") ||
        lower.contains('\\') || lower.contains("@import") || lower.contains("-moz-") ||
        lower.contains("-webkit-") { return Err(unsupported("external-or-unsafe-value")); }
    let mut rest = lower.as_str();
    while let Some(pos) = rest.find("url(") {
        rest = &rest[pos + 4..];
        let end = rest.find(')').ok_or_else(|| invalid("invalid-fragment-reference"))?;
        let target = rest[..end].trim().trim_matches(['\'', '"']);
        if !valid_fragment(target) { return Err(unsupported("external-reference")); }
        rest = &rest[end + 1..];
    }
    Ok(())
}

fn valid_fragment(value: &str) -> bool {
    value.strip_prefix('#').is_some_and(|id| !id.is_empty() && id.len() <= 128 &&
        id.bytes().all(|b| b.is_ascii_alphanumeric() || b == b'_' || b == b'-' || b == b'.'))
}

fn validate_stylesheet(css: &str) -> Result<(), Failure> {
    if css.len() > 65_536 || css.contains("/*") || css.contains('@') || css.contains("<!--") {
        return Err(unsupported("unsupported-stylesheet"));
    }
    let mut rest = css.trim();
    let mut count = 0;
    while !rest.is_empty() {
        let open = rest.find('{').ok_or_else(|| invalid("invalid-stylesheet"))?;
        let end = rest[open + 1..].find('}').ok_or_else(|| invalid("invalid-stylesheet"))? + open + 1;
        let selector = rest[..open].trim();
        if selector.is_empty() || selector.len() > 256 || !selector.bytes().all(|b|
            b.is_ascii_alphanumeric() || b"#._- ,>*".contains(&b)) {
            return Err(unsupported("unsupported-css-selector"));
        }
        css_declarations(&rest[open + 1..end])?;
        rest = rest[end + 1..].trim();
        count += 1;
        if count > 128 { return Err(resource("css-rule-limit")); }
    }
    Ok(())
}

fn allowed_element(name: &str) -> bool {
    matches!(name,
        "svg" | "g" | "defs" | "symbol" | "use" | "style" | "title" | "desc" |
        "rect" | "circle" | "ellipse" | "line" | "polyline" | "polygon" | "path" |
        "linearGradient" | "radialGradient" | "stop" | "clipPath" | "mask" |
        "pattern" | "filter" | "feGaussianBlur" | "feOffset" | "feMerge" |
        "feMergeNode" | "feColorMatrix" | "feComposite" | "feBlend" |
        "feFlood" | "feTile" | "feComponentTransfer" | "feFuncR" | "feFuncG" |
        "feFuncB" | "feFuncA" | "feMorphology" | "text" | "tspan" | "image")
}

fn allowed_attribute(name: &str) -> bool {
    matches!(name,
        "id" | "class" | "style" | "href" | "version" | "viewBox" | "preserveAspectRatio" |
        "width" | "height" | "x" | "y" | "x1" | "x2" | "y1" | "y2" | "cx" | "cy" |
        "r" | "rx" | "ry" | "fx" | "fy" | "fr" | "dx" | "dy" | "d" | "points" | "transform" | "transform-origin" |
        "gradientTransform" | "gradientUnits" | "spreadMethod" | "offset" | "filterUnits" |
        "primitiveUnits" | "maskUnits" | "maskContentUnits" | "clipPathUnits" |
        "patternUnits" | "patternContentUnits" | "patternTransform" | "result" | "in" | "in2" |
        "stdDeviation" | "operator" | "mode" | "type" | "values" | "k1" | "k2" |
        "k3" | "k4" | "slope" | "intercept" | "amplitude" | "exponent" |
        "edgeMode" | "radius" | "baseFrequency" | "numOctaves" | "seed" |
        "fill" | "fill-opacity" | "fill-rule" | "stroke" | "stroke-width" | "stroke-opacity" |
        "stroke-linecap" | "stroke-linejoin" | "stroke-miterlimit" | "stroke-dasharray" |
        "stroke-dashoffset" | "opacity" | "display" | "visibility" | "color" | "clip-path" |
        "clip-rule" | "mask" | "filter" | "stop-color" | "stop-opacity" |
        "font-family" | "font-size" | "font-style" | "font-weight" | "font-stretch" |
        "text-anchor" | "letter-spacing" | "word-spacing" | "paint-order" |
        "shape-rendering" | "text-rendering" | "image-rendering" | "dominant-baseline" |
        "baseline-shift" | "text-decoration" | "vector-effect" | "overflow" | "space")
}

fn validate_geometry(name: &str, key: &str, value: &str) -> Result<(), Failure> {
    if key == "d" && name == "path" {
        let mut count = 0usize;
        for segment in svgtypes::PathParser::from(value) {
            let segment = segment.map_err(|_| invalid("invalid-path-data"))?;
            if !finite_segment(segment) { return Err(invalid("invalid-path-number")); }
            count += 1;
            if count > 50_000 { return Err(resource("path-segment-limit")); }
        }
        if count == 0 { return Err(invalid("empty-path-data")); }
    }
    if key == "points" && matches!(name, "polygon" | "polyline") {
        let mut count = 0usize;
        for number in svgtypes::NumberListParser::from(value) {
            if !number.map_err(|_| invalid("invalid-points"))?.is_finite() { return Err(invalid("invalid-points")); }
            count += 1;
        }
        if count % 2 != 0 || count < if name == "polygon" { 6 } else { 4 } {
            return Err(invalid("invalid-points-count"));
        }
    }
    if matches!(key, "transform" | "gradientTransform" | "patternTransform") {
        let mut count = 0;
        for token in svgtypes::TransformListParser::from(value) {
            let token = token.map_err(|_| invalid("invalid-transform"))?;
            if !finite_transform(token) { return Err(invalid("invalid-transform-number")); }
            count += 1;
            if count > 64 { return Err(resource("transform-count-limit")); }
        }
        if value.trim().is_empty() { return Err(invalid("empty-transform")); }
    }
    if key == "viewBox" {
        let numbers: Vec<f64> = svgtypes::NumberListParser::from(value)
            .map(|n| n.map_err(|_| invalid("invalid-viewbox"))).collect::<Result<_, _>>()?;
        if numbers.len() != 4 || numbers.iter().any(|n| !n.is_finite()) || numbers[2] <= 0.0 || numbers[3] <= 0.0 {
            return Err(invalid("invalid-viewbox"));
        }
    }
    if matches!(key, "x" | "y" | "x1" | "y1" | "x2" | "y2" | "cx" | "cy" |
        "r" | "rx" | "ry" | "fx" | "fy" | "fr" | "dx" | "dy" | "width" | "height") &&
        !matches!(name, "filter" | "feGaussianBlur" | "feOffset") {
        let length: svgtypes::Length = value.parse().map_err(|_| invalid("invalid-length"))?;
        if !length.number.is_finite() { return Err(invalid("invalid-length")); }
    }
    Ok(())
}

fn finite_transform(token: svgtypes::TransformListToken) -> bool {
    use svgtypes::TransformListToken as Token;
    let values: Vec<f64> = match token {
        Token::Matrix { a, b, c, d, e, f } => vec![a, b, c, d, e, f],
        Token::Translate { tx, ty } => vec![tx, ty],
        Token::Scale { sx, sy } => vec![sx, sy],
        Token::Rotate { angle } | Token::SkewX { angle } | Token::SkewY { angle } => vec![angle],
    };
    values.into_iter().all(f64::is_finite)
}

fn finite_segment(segment: svgtypes::PathSegment) -> bool {
    use svgtypes::PathSegment as Segment;
    let values: Vec<f64> = match segment {
        Segment::MoveTo { x, y, .. } | Segment::LineTo { x, y, .. } |
        Segment::SmoothQuadratic { x, y, .. } => vec![x, y],
        Segment::HorizontalLineTo { x, .. } => vec![x],
        Segment::VerticalLineTo { y, .. } => vec![y],
        Segment::CurveTo { x1, y1, x2, y2, x, y, .. } => vec![x1, y1, x2, y2, x, y],
        Segment::SmoothCurveTo { x2, y2, x, y, .. } => vec![x2, y2, x, y],
        Segment::Quadratic { x1, y1, x, y, .. } => vec![x1, y1, x, y],
        Segment::EllipticalArc { rx, ry, x_axis_rotation, x, y, .. } => vec![rx, ry, x_axis_rotation, x, y],
        Segment::ClosePath { .. } => vec![],
    };
    values.into_iter().all(f64::is_finite)
}

fn preflight(bytes: &[u8]) -> Result<Admission, Failure> {
    if bytes.len() > MAX_INPUT { return Err(resource("input-size-limit")); }
    let text = std::str::from_utf8(bytes).map_err(|_| invalid("utf8-required"))?;
    let lower_xml = text.to_ascii_lowercase();
    if lower_xml.contains("<!doctype") || lower_xml.contains("<!entity") {
        return Err(unsupported("dtd-or-entity-refused"));
    }
    let document = usvg::roxmltree::Document::parse(text).map_err(|_| invalid("invalid-xml"))?;
    let root = document.root_element();
    if root.tag_name().name() != "svg" || root.tag_name().namespace() != Some(SVG_NS) {
        return Err(invalid("svg-root-required"));
    }
    let mut admission = Admission::default();
    let mut ids = HashMap::new();
    let mut references = Vec::new();
    let mut url_references = Vec::new();
    let mut elements = 0;
    let mut filter_count = 0;
    let mut primitives = 0;
    for node in document.descendants() {
        if node.is_pi() { return Err(unsupported("processing-instruction-refused")); }
        if !node.is_element() { continue; }
        elements += 1;
        if elements > MAX_ELEMENTS || node.ancestors().filter(|n| n.is_element()).count() > MAX_DEPTH {
            return Err(resource("xml-complexity-limit"));
        }
        if node.tag_name().namespace() != Some(SVG_NS) || !allowed_element(node.tag_name().name()) {
            return Err(unsupported("unsupported-element"));
        }
        let name = node.tag_name().name();
        if name == "style" {
            if node.children().filter(|child| child.is_text()).count() > 1 || node.children().any(|child| child.is_element()) {
                return Err(unsupported("unsupported-stylesheet-content"));
            }
            let css = node.text().unwrap_or("");
            validate_stylesheet(css)?;
            collect_url_references(css, &mut url_references);
        }
        if name == "text" || name == "tspan" { admission.text = true; }
        if name == "use" { admission.uses += 1; if admission.uses > 256 { return Err(resource("use-count-limit")); } }
        if name == "filter" { filter_count += 1; admission.filtered = true; if filter_count > 4 { return Err(resource("filter-count-limit")); } }
        if name.starts_with("fe") { primitives += 1; if primitives > 16 { return Err(resource("filter-primitive-limit")); } }
        if let Some(id) = node.attribute("id") {
            if id.len() > 128 || !id.bytes().all(|b| b.is_ascii_alphanumeric() || b"_-.".contains(&b)) {
                return Err(unsupported("unsupported-id"));
            }
            if ids.insert(id, node).is_some() { return Err(invalid("duplicate-id")); }
        }
        for attr in node.attributes() {
            let key = attr.name();
            let value = attr.value();
            if key.starts_with("on") || key == "base" || key == "xml:base" { return Err(unsupported("active-attribute-refused")); }
            if attr.namespace().is_some_and(|ns| ns != XLINK_NS && ns != XML_NS) || !allowed_attribute(key) {
                return Err(unsupported("unsupported-attribute"));
            }
            if attr.namespace() == Some(XML_NS) && (key != "space" || !matches!(value, "default" | "preserve")) {
                return Err(unsupported("unsupported-xml-attribute"));
            }
            if attr.namespace() == Some(XLINK_NS) && key != "href" { return Err(unsupported("unsupported-xlink-attribute")); }
            validate_geometry(name, key, value)?;
            if key == "style" { css_declarations(value)?; collect_url_references(value, &mut url_references); }
            else if key == "href" {
                if name == "image" {
                    admission.images += 1;
                    if node.ancestors().skip(1).all(|ancestor| !ancestor.is_element() || matches!(ancestor.tag_name().name(), "svg" | "g")) {
                        admission.direct_images += 1;
                    }
                    admission.image_pixels += validate_image(value)?;
                }
                else if valid_fragment(value) { references.push((node, &value[1..])); }
                else { return Err(unsupported("external-reference-refused")); }
            } else { safe_value(value)?; collect_url_references(value, &mut url_references); }
            if name == "filter" && matches!(key, "x" | "y" | "width" | "height") {
                let number: f64 = value.strip_suffix('%').ok_or_else(|| unsupported("filter-region-unit"))?
                    .parse().map_err(|_| invalid("invalid-filter-region"))?;
                if !number.is_finite() || (matches!(key, "x" | "y") && !(-50.0..=50.0).contains(&number)) ||
                    (matches!(key, "width" | "height") && !(0.0..=200.0).contains(&number)) {
                    return Err(resource("filter-region-limit"));
                }
            }
            if name == "filter" && matches!(key, "filterUnits" | "primitiveUnits") && value != "objectBoundingBox" {
                return Err(unsupported("filter-units-refused"));
            }
            if name == "pattern" && matches!(key, "width" | "height") {
                let number: f64 = value.parse().map_err(|_| unsupported("pattern-size-unit"))?;
                if !number.is_finite() || !(0.0..=1024.0).contains(&number) {
                    return Err(resource("pattern-size-limit"));
                }
            }
            if name == "feGaussianBlur" && key == "stdDeviation" {
                if value.split_ascii_whitespace().any(|s| s.parse::<f64>().map_or(true, |v| !v.is_finite() || !(0.0..=8.0).contains(&v))) {
                    return Err(resource("blur-limit"));
                }
            }
            if name == "feOffset" && matches!(key, "dx" | "dy") &&
                value.parse::<f64>().map_or(true, |v| !v.is_finite() || !(-64.0..=64.0).contains(&v)) {
                return Err(resource("offset-limit"));
            }
            if name == "feMorphology" && key == "radius" &&
                value.split_ascii_whitespace().any(|s| s.parse::<f64>().map_or(true, |v| !v.is_finite() || !(0.0..=8.0).contains(&v))) {
                return Err(resource("morphology-radius-limit"));
            }
        }
    }
    if admission.images > MAX_EMBEDDED || admission.image_pixels > MAX_TOTAL_EMBEDDED_PIXELS {
        return Err(resource("embedded-image-limit"));
    }
    if document.descendants().filter(|n| n.is_element() && n.tag_name().name() == "image").count() != admission.images {
        return Err(unsupported("embedded-image-href-required"));
    }
    for (_, id) in &references { if !ids.contains_key(id) { return Err(unsupported("missing-fragment-reference")); } }
    for id in &url_references { if !ids.contains_key(id.as_str()) { return Err(unsupported("missing-paint-reference")); } }
    validate_reference_cycles(&ids)?;
    // Use expansion is capped before usvg resolves recursive local references.
    for (node, _) in references.iter().filter(|(n, _)| n.tag_name().name() == "use") {
        let mut visited = HashSet::new();
        let mut count = 0;
        expand_use(*node, &ids, &mut visited, &mut count)?;
    }
    Ok(admission)
}

fn collect_url_references(value: &str, references: &mut Vec<String>) {
    let lower = value.to_ascii_lowercase();
    let mut at = 0;
    while let Some(start) = lower[at..].find("url(") {
        let start = at + start + 4;
        let Some(end) = value[start..].find(')') else { break; };
        let target = value[start..start + end].trim().trim_matches(['\'', '"']);
        if let Some(id) = target.strip_prefix('#') { references.push(id.to_string()); }
        at = start + end + 1;
    }
}

fn validate_reference_cycles<'a, 'input>(ids: &HashMap<&'input str, usvg::roxmltree::Node<'a, 'input>>) -> Result<(), Failure> {
    let mut visiting = HashSet::new();
    let mut complete = HashSet::new();
    for id in ids.keys() { visit_reference(id, ids, &mut visiting, &mut complete)?; }
    Ok(())
}

fn visit_reference<'a, 'input>(id: &'input str, ids: &HashMap<&'input str, usvg::roxmltree::Node<'a, 'input>>,
    visiting: &mut HashSet<&'input str>, complete: &mut HashSet<&'input str>) -> Result<(), Failure> {
    if complete.contains(id) { return Ok(()); }
    if !visiting.insert(id) { return Err(unsupported("cyclic-local-reference")); }
    if visiting.len() > 64 { return Err(resource("reference-depth-limit")); }
    let node = ids.get(id).ok_or_else(|| unsupported("missing-local-reference"))?;
    for descendant in node.descendants().filter(|n| n.is_element()) {
        for attr in descendant.attributes() {
            if attr.name() == "href" && descendant.tag_name().name() != "image" {
                if let Some(target) = attr.value().strip_prefix('#') {
                    let (key, _) = ids.get_key_value(target).ok_or_else(|| unsupported("missing-local-reference"))?;
                    visit_reference(key, ids, visiting, complete)?;
                }
            } else {
                let mut references = Vec::new();
                collect_url_references(attr.value(), &mut references);
                for target in references {
                    let (key, _) = ids.get_key_value(target.as_str()).ok_or_else(|| unsupported("missing-local-reference"))?;
                    visit_reference(key, ids, visiting, complete)?;
                }
            }
        }
    }
    visiting.remove(id);
    complete.insert(id);
    Ok(())
}

fn expand_use<'a, 'input>(node: usvg::roxmltree::Node<'a, 'input>, ids: &HashMap<&'input str, usvg::roxmltree::Node<'a, 'input>>,
    visited: &mut HashSet<usvg::roxmltree::NodeId>, count: &mut usize) -> Result<(), Failure> {
    *count += 1;
    if *count > 8_000 || visited.len() > 32 { return Err(resource("use-expansion-limit")); }
    if !visited.insert(node.id()) { return Err(unsupported("cyclic-use")); }
    let href = node.attribute("href").or_else(|| node.attribute((XLINK_NS, "href"))).ok_or_else(|| unsupported("use-href-required"))?;
    let target = ids.get(&href[1..]).ok_or_else(|| unsupported("missing-use-target"))?;
    for descendant in target.descendants().filter(|n| n.is_element()) {
        *count += 1;
        if *count > 8_000 { return Err(resource("use-expansion-limit")); }
        if descendant.tag_name().name() == "use" { expand_use(descendant, ids, visited, count)?; }
    }
    visited.remove(&node.id());
    Ok(())
}

fn validate_image(href: &str) -> Result<u64, Failure> {
    let (kind, encoded) = href.strip_prefix("data:image/png;base64,").map(|x| ("png", x))
        .or_else(|| href.strip_prefix("data:image/jpeg;base64,").map(|x| ("jpeg", x)))
        .ok_or_else(|| unsupported("embedded-image-type"))?;
    if encoded.len() > 520_000 { return Err(resource("embedded-encoded-size")); }
    let data = base64::engine::general_purpose::STANDARD.decode(encoded).map_err(|_| invalid("embedded-base64"))?;
    if data.len() > MAX_EMBEDDED_BYTES { return Err(resource("embedded-decoded-size")); }
    let (width, height) = if kind == "png" { validate_png(&data)? } else { validate_jpeg(&data)? };
    let pixels = u64::from(width) * u64::from(height);
    if width == 0 || height == 0 || width > 2_048 || height > 2_048 || pixels > MAX_EMBEDDED_PIXELS {
        return Err(resource("embedded-pixel-limit"));
    }
    Ok(pixels)
}

fn validate_png(data: &[u8]) -> Result<(u32, u32), Failure> {
    if !data.starts_with(b"\x89PNG\r\n\x1a\n") { return Err(invalid("embedded-png-signature")); }
    let mut at = 8usize;
    while at + 12 <= data.len() {
        let length = u32::from_be_bytes(data[at..at + 4].try_into().unwrap()) as usize;
        let end = at.checked_add(12).and_then(|x| x.checked_add(length)).ok_or_else(|| invalid("embedded-png-chunk"))?;
        if end > data.len() { return Err(invalid("embedded-png-chunk")); }
        let kind = &data[at + 4..at + 8];
        if matches!(kind, b"acTL" | b"fcTL" | b"fdAT" | b"iCCP" | b"gAMA" | b"cHRM" | b"eXIf") {
            if kind == b"acTL" || kind == b"fcTL" || kind == b"fdAT" { return Err(unsupported("embedded-animation")); }
            return Err(unsupported("embedded-profile-or-orientation"));
        }
        at = end;
        if kind == b"IEND" { break; }
    }
    let mut decoder = png::Decoder::new(Cursor::new(data));
    decoder.set_limits(png::Limits { bytes: 8 * 1024 * 1024 });
    let mut reader = decoder.read_info().map_err(|_| invalid("embedded-png"))?;
    let info = reader.info();
    if info.is_animated() { return Err(unsupported("embedded-animation")); }
    if info.bit_depth != png::BitDepth::Eight { return Err(unsupported("embedded-png-precision")); }
    let (width, height) = (info.width, info.height);
    if u64::from(width) * u64::from(height) > MAX_EMBEDDED_PIXELS { return Err(resource("embedded-pixel-limit")); }
    let size = reader.output_buffer_size().ok_or_else(|| invalid("embedded-png-buffer"))?;
    if size > 4 * 1024 * 1024 { return Err(resource("embedded-raster-limit")); }
    let mut pixels = vec![0; size];
    reader.next_frame(&mut pixels).map_err(|_| invalid("embedded-png"))?;
    reader.finish().map_err(|_| invalid("embedded-png"))?;
    Ok((width, height))
}

fn validate_jpeg(data: &[u8]) -> Result<(u32, u32), Failure> {
    if !data.starts_with(&[0xff, 0xd8]) { return Err(invalid("embedded-jpeg-signature")); }
    let mut at = 2usize;
    while at + 4 <= data.len() {
        if data[at] != 0xff { break; }
        let marker = data[at + 1];
        if marker == 0xda || marker == 0xd9 { break; }
        if marker == 0xff { at += 1; continue; }
        let length = u16::from_be_bytes([data[at + 2], data[at + 3]]) as usize;
        if length < 2 || at + 2 + length > data.len() { return Err(invalid("embedded-jpeg-marker")); }
        if marker == 0xe1 || marker == 0xe2 || marker == 0xed || marker == 0xee {
            return Err(unsupported("embedded-jpeg-metadata"));
        }
        at += 2 + length;
    }
    let mut decoder = zune_jpeg::JpegDecoder::new(Cursor::new(data));
    decoder.decode_headers().map_err(|_| invalid("embedded-jpeg"))?;
    let info = decoder.info().ok_or_else(|| invalid("embedded-jpeg"))?;
    let (width, height) = (u32::from(info.width), u32::from(info.height));
    if u64::from(width) * u64::from(height) > MAX_EMBEDDED_PIXELS { return Err(resource("embedded-pixel-limit")); }
    if decoder.output_buffer_size().ok_or_else(|| invalid("embedded-jpeg-buffer"))? > 4 * 1024 * 1024 {
        return Err(resource("embedded-raster-limit"));
    }
    decoder.decode().map_err(|_| invalid("embedded-jpeg"))?;
    Ok((width, height))
}

#[link(name = "bcrypt")]
unsafe extern "system" {
    fn BCryptOpenAlgorithmProvider(handle: *mut isize, algorithm: *const u16, implementation: *const u16, flags: u32) -> i32;
    fn BCryptHash(handle: isize, secret: *const u8, secret_len: u32, input: *const u8, input_len: u32, output: *mut u8, output_len: u32) -> i32;
    fn BCryptCloseAlgorithmProvider(handle: isize, flags: u32) -> i32;
}

#[link(name = "kernel32")]
unsafe extern "system" {
    fn GetWindowsDirectoryW(buffer: *mut u16, size: u32) -> u32;
}

fn sha256(bytes: &[u8]) -> Result<[u8; 32], Failure> {
    let algorithm: Vec<u16> = "SHA256\0".encode_utf16().collect();
    let mut handle = 0isize;
    // CNG is part of supported Windows. Hash failures are engine faults.
    unsafe {
        if BCryptOpenAlgorithmProvider(&mut handle, algorithm.as_ptr(), std::ptr::null(), 0) < 0 { return Err(engine("hash-initialization")); }
        let mut output = [0u8; 32];
        let status = BCryptHash(handle, std::ptr::null(), 0, bytes.as_ptr(), bytes.len() as u32, output.as_mut_ptr(), 32);
        BCryptCloseAlgorithmProvider(handle, 0);
        if status < 0 { return Err(engine("hash-failure")); }
        Ok(output)
    }
}

fn font_name(db: &usvg::fontdb::Database, id: usvg::fontdb::ID) -> String {
    db.face(id).and_then(|face| face.families.first()).map(|f| f.0.clone()).unwrap_or_else(|| "unknown".into())
}

fn sanitize_notice(value: String) -> String {
    value.chars().filter(|c| !c.is_control()).take(256).collect()
}

fn json_string(value: &str) -> String {
    let mut result = String::from("\"");
    for ch in value.chars() {
        match ch { '\\' => result.push_str("\\\\"), '"' => result.push_str("\\\""), _ => result.push(ch) }
    }
    result.push('"');
    result
}

fn run() -> Result<(), Failure> {
    log::set_logger(&WARNING_LOGGER).map_err(|_| engine("logger-initialization"))?;
    log::set_max_level(log::LevelFilter::Warn);
    let args: Vec<String> = env::args().collect();
    let requested = match args.as_slice() {
        [_, command] if command == "inspect" => None,
        [_, command, width, height] if command == "render" => {
            let width: u32 = width.parse().map_err(|_| invalid("invalid-render-width"))?;
            let height: u32 = height.parse().map_err(|_| invalid("invalid-render-height"))?;
            Some((width, height))
        }
        _ => return Err(invalid("usage-inspect-or-render-width-height")),
    };
    let mut bytes = Vec::new();
    io::stdin().take((MAX_INPUT + 1) as u64).read_to_end(&mut bytes).map_err(|_| engine("stdin-read"))?;
    let admission = preflight(&bytes)?;
    let mut options = usvg::Options::default();
    options.resources_dir = None;
    options.font_family = "Arial".into();
    options.image_href_resolver = usvg::ImageHrefResolver {
        resolve_data: Box::new(|mime, bytes, _| match mime {
            "image/png" => Some(usvg::ImageKind::PNG(bytes)),
            "image/jpeg" => Some(usvg::ImageKind::JPEG(bytes)),
            _ => None,
        }),
        resolve_string: Box::new(|_, _| None),
    };
    let mut windows_buffer = [0u16; 1024];
    let windows_length = unsafe { GetWindowsDirectoryW(windows_buffer.as_mut_ptr(), windows_buffer.len() as u32) } as usize;
    if windows_length == 0 || windows_length >= windows_buffer.len() { return Err(engine("windows-root-unavailable")); }
    let windows = PathBuf::from(String::from_utf16(&windows_buffer[..windows_length]).map_err(|_| engine("windows-root-unavailable"))?);
    if !windows.is_absolute() { return Err(engine("invalid-windows-root")); }
    let font_files = ["arial.ttf", "arialbd.ttf", "ariali.ttf", "arialbi.ttf",
        "segoeui.ttf", "segoeuib.ttf", "segoeuii.ttf", "times.ttf", "timesbd.ttf",
        "timesi.ttf", "cour.ttf", "courbd.ttf", "couri.ttf"];
    let mut font_identity = Vec::new();
    for file in font_files {
        let path = windows.join("Fonts").join(file);
        let size = fs::metadata(&path).map_err(|_| engine("fixed-font-unavailable"))?.len();
        if size == 0 || size > 8_000_000 { return Err(resource("fixed-font-size-limit")); }
        let data = fs::read(&path).map_err(|_| engine("fixed-font-read"))?;
        font_identity.extend_from_slice(file.as_bytes());
        font_identity.extend_from_slice(&sha256(&data)?);
        options.fontdb_mut().load_font_data(data);
    }
    options.fontdb_mut().set_serif_family("Times New Roman");
    options.fontdb_mut().set_sans_serif_family("Arial");
    options.fontdb_mut().set_monospace_family("Courier New");
    let fingerprint = sha256(&font_identity)?.iter().map(|b| format!("{b:02x}")).collect::<String>();
    let notices = Arc::new(Mutex::new(Vec::<String>::new()));
    let missing_glyph = Arc::new(AtomicBool::new(false));
    let notice_limit = Arc::new(AtomicBool::new(false));
    let selected_notices = Arc::clone(&notices);
    let fallback_notices = Arc::clone(&notices);
    let fallback_missing_glyph = Arc::clone(&missing_glyph);
    let selection_notice_limit = Arc::clone(&notice_limit);
    let fallback_notice_limit = Arc::clone(&notice_limit);
    let select = usvg::FontResolver::default_font_selector();
    let fallback = usvg::FontResolver::default_fallback_selector();
    options.font_resolver = usvg::FontResolver {
        select_font: Box::new(move |font, db| {
            let chosen = select(font, db);
            let requested_names = font.families().iter().map(ToString::to_string).collect::<Vec<_>>();
            let requested = requested_names.join(" or ").replace('"', "");
            let resolved = chosen.map(|id| font_name(db, id)).unwrap_or_else(|| "none".into());
            let is_expected = requested_names.iter().any(|family| {
                let family = family.trim_matches('"');
                family.eq_ignore_ascii_case(&resolved) ||
                    (family.eq_ignore_ascii_case("serif") && resolved.eq_ignore_ascii_case("Times New Roman")) ||
                    (family.eq_ignore_ascii_case("sans-serif") && resolved.eq_ignore_ascii_case("Arial")) ||
                    (family.eq_ignore_ascii_case("monospace") && resolved.eq_ignore_ascii_case("Courier New"))
            });
            if chosen.is_none() || requested.len() > 120 { selection_notice_limit.store(true, Ordering::Relaxed); }
            if is_expected { return chosen; }
            let notice = sanitize_notice(format!("{requested} will use {resolved}."));
            let mut list = selected_notices.lock().unwrap();
            if !list.contains(&notice) {
                if list.len() < 32 { list.push(notice); }
                else { selection_notice_limit.store(true, Ordering::Relaxed); }
            }
            chosen
        }),
        select_fallback: Box::new(move |ch, used, db| {
            let chosen = fallback(ch, used, db);
            if chosen.is_none() { fallback_missing_glyph.store(true, Ordering::Relaxed); }
            let resolved = chosen.map(|id| font_name(db, id)).unwrap_or_else(|| "none".into());
            let notice = sanitize_notice(format!("U+{:04X} uses {resolved} glyph fallback.", ch as u32));
            let mut list = fallback_notices.lock().unwrap();
            if !list.contains(&notice) {
                if list.len() < 32 { list.push(notice); }
                else { fallback_notice_limit.store(true, Ordering::Relaxed); }
            }
            chosen
        }),
    };
    let tree = usvg::Tree::from_data(&bytes, &options).map_err(|_| invalid("svg-parse"))?;
    if missing_glyph.load(Ordering::Relaxed) { return Err(unsupported("unsupported-text-glyph")); }
    if notice_limit.load(Ordering::Relaxed) { return Err(resource("font-notice-limit")); }
    if RENDER_WARNING.load(Ordering::Relaxed) { return Err(unsupported("svg-feature-not-rendered")); }
    let intrinsic_w = f64::from(tree.size().width());
    let intrinsic_h = f64::from(tree.size().height());
    if !intrinsic_w.is_finite() || !intrinsic_h.is_finite() || intrinsic_w <= 0.0 || intrinsic_h <= 0.0 ||
        intrinsic_w > 1_000_000.0 || intrinsic_h > 1_000_000.0 { return Err(resource("intrinsic-size-limit")); }
    if count_images(tree.root()) < admission.direct_images { return Err(unsupported("embedded-image-omitted")); }
    if admission.text && !tree.has_text_nodes() { return Err(unsupported("text-omitted")); }
    let limits = if admission.filtered { Limits { side: 1024, pixels: 1_000_000 } }
        else { Limits { side: 4096, pixels: 16_000_000 } };
    let (width, height) = requested.unwrap_or((intrinsic_w.ceil() as u32, intrinsic_h.ceil() as u32));
    if let Some((width, height)) = requested {
        if width == 0 || height == 0 || width > limits.side || height > limits.side ||
            u64::from(width) * u64::from(height) > limits.pixels { return Err(resource("render-dimension-limit")); }
        let rounded_h = (f64::from(width) * intrinsic_h / intrinsic_w).round().max(1.0);
        let rounded_w = (f64::from(height) * intrinsic_w / intrinsic_h).round().max(1.0);
        if f64::from(height) != rounded_h && f64::from(width) != rounded_w {
            return Err(invalid("render-aspect-ratio"));
        }
    }
    let notice_list = notices.lock().unwrap();
    let serialized_notices = notice_list.iter().map(|s| json_string(s)).collect::<Vec<_>>().join(",");
    let header = format!("{{\"Abi\":1,\"IntrinsicWidth\":{intrinsic_w},\"IntrinsicHeight\":{intrinsic_h},\"Width\":{width},\"Height\":{height},\"MaximumSide\":{},\"MaximumPixels\":{},\"FontFingerprint\":\"{fingerprint}\",\"FontNotices\":[{serialized_notices}]}}", limits.side, limits.pixels);
    drop(notice_list);
    let mut stdout = io::stdout().lock();
    if requested.is_none() {
        stdout.write_all(header.as_bytes()).map_err(|_| engine("stdout-write"))?;
        return Ok(());
    }
    let mut pixmap = tiny_skia::Pixmap::new(width, height).ok_or_else(|| resource("pixmap-allocation"))?;
    let scale = (f64::from(width) / intrinsic_w).min(f64::from(height) / intrinsic_h) as f32;
    resvg::render(&tree, tiny_skia::Transform::from_scale(scale, scale), &mut pixmap.as_mut());
    if RENDER_WARNING.load(Ordering::Relaxed) { return Err(unsupported("svg-render-warning")); }
    let header_bytes = header.as_bytes();
    stdout.write_all(&(header_bytes.len() as u32).to_le_bytes()).map_err(|_| engine("stdout-write"))?;
    stdout.write_all(header_bytes).map_err(|_| engine("stdout-write"))?;
    for pixel in pixmap.data_mut().chunks_exact_mut(4) {
        let alpha = pixel[3];
        let rgba = if alpha == 0 { [0, 0, 0, 0] } else {
            let unpremultiply = |v: u8| ((u32::from(v) * 255 + u32::from(alpha) / 2) / u32::from(alpha)).min(255) as u8;
            [unpremultiply(pixel[0]), unpremultiply(pixel[1]), unpremultiply(pixel[2]), alpha]
        };
        pixel.copy_from_slice(&rgba);
    }
    stdout.write_all(pixmap.data()).map_err(|_| engine("stdout-write"))?;
    Ok(())
}

fn count_images(group: &usvg::Group) -> usize {
    group.children().iter().map(|node| match node {
        usvg::Node::Image(_) => 1,
        usvg::Node::Group(group) => count_images(group),
        _ => 0,
    }).sum()
}

fn main() {
    if let Err(failure) = run() {
        eprintln!("{}", failure.message);
        std::process::exit(failure.code);
    }
}
