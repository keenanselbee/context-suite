// Included inside PdfDark.cpp's anonymous namespace, after Require and Unsupported.
// Keep this policy independent of paint rewriting: it admits a finite passive PDF
// subset and snapshots its logical relationships before any resources are cloned.

void ValidatePassiveAcroForm(QPDF& pdf)
{
    auto form = pdf.getRoot().getKey("/AcroForm");
    if (form.isNull()) return;
    Require(form.isDictionary(), "Invalid PDF form shell.");
    for (auto const& key : form.getKeys())
        if (key != "/Fields" && key != "/DA" && key != "/DR" &&
            key != "/NeedAppearances" && key != "/SigFlags" && key != "/Q" && key != "/CO")
            Unsupported("Interactive PDF forms are unsupported.");
    auto fields = form.getKey("/Fields");
    Require(fields.isNull() || fields.isArray(), "Invalid PDF form fields.");
    if (fields.isArray() && fields.getArrayNItems() != 0)
        Unsupported("Interactive PDF forms are unsupported.");
    auto order = form.getKey("/CO");
    Require(order.isNull() || order.isArray(), "Invalid PDF form calculation order.");
    if (order.isArray() && order.getArrayNItems() != 0)
        Unsupported("Interactive PDF forms are unsupported.");
    auto signatures = form.getKey("/SigFlags");
    Require(signatures.isNull() || signatures.isInteger(), "Invalid PDF form signature flags.");
    if (signatures.isInteger() && signatures.getIntValue() != 0)
        Unsupported("Signed PDF form declarations are unsupported.");
    auto appearances = form.getKey("/NeedAppearances");
    Require(appearances.isNull() || appearances.isBool(), "Invalid PDF form appearance flag.");
    auto alignment = form.getKey("/Q");
    Require(alignment.isNull() || alignment.isInteger() && alignment.getIntValue() >= 0 &&
        alignment.getIntValue() <= 2, "Invalid PDF form alignment.");
    auto appearance = form.getKey("/DA");
    Require(appearance.isNull() || appearance.isString(), "Invalid PDF form default appearance.");
    if (appearance.isString()) RequireLimit(appearance.getStringValue().size() <= 65536,
        "PDF form default appearance budget exceeded.");
    auto resources = form.getKey("/DR");
    if (resources.isNull()) return;
    Require(resources.isDictionary(), "Invalid PDF form resources.");
    for (auto const& key : resources.getKeys()) {
        auto value = resources.getKey(key);
        if (key == "/Font") {
            Require(value.isDictionary(), "Invalid PDF form font resources.");
            RequireLimit(value.getKeys().size() <= 256, "PDF form resource budget exceeded.");
            for (auto const& font : value.getKeys())
                Require(value.getKey(font).isDictionary() &&
                    value.getKey(font).getKey("/Type").isNameAndEquals("/Font"),
                    "Invalid PDF form font resource.");
        } else if (key == "/Encoding") {
            Require(value.isDictionary(), "Invalid PDF form encoding resources.");
            RequireLimit(value.getKeys().size() <= 256, "PDF form resource budget exceeded.");
            for (auto const& encodingName : value.getKeys()) {
                auto encoding = value.getKey(encodingName);
                Require(encoding.isDictionary() && encoding.getKey("/Type").isNameAndEquals("/Encoding"),
                    "Invalid PDF form encoding resource.");
                for (auto const& entry : encoding.getKeys())
                    Require(entry == "/Type" || entry == "/BaseEncoding" || entry == "/Differences",
                        "Invalid PDF form encoding key.");
                auto base = encoding.getKey("/BaseEncoding");
                Require(base.isNull() || base.isNameAndEquals("/WinAnsiEncoding") ||
                    base.isNameAndEquals("/MacRomanEncoding") || base.isNameAndEquals("/MacExpertEncoding"),
                    "Invalid PDF form base encoding.");
                auto differences = encoding.getKey("/Differences");
                if (differences.isNull()) {
                    Require(!base.isNull(), "PDF form encoding has no mapping.");
                    continue;
                }
                Require(differences.isArray(), "Invalid PDF form encoding differences.");
                RequireLimit(differences.getArrayNItems() <= 512,
                    "PDF form encoding difference budget exceeded.");
                int position = -1;
                std::set<int> used;
                for (int i = 0; i < differences.getArrayNItems(); ++i) {
                    auto item = differences.getArrayItem(i);
                    if (item.isInteger()) {
                        Require(item.getIntValue() >= 0 && item.getIntValue() <= 255,
                            "Invalid PDF form encoding position.");
                        position = static_cast<int>(item.getIntValue());
                    } else {
                        Require(item.isName() && position >= 0 && position <= 255 &&
                            used.insert(position).second, "Invalid PDF form encoding glyph.");
                        RequireLimit(item.getName().size() <= 4096,
                            "PDF form encoding glyph budget exceeded.");
                        ++position;
                    }
                }
            }
        } else if (key == "/ProcSet") {
            Require(value.isArray() && value.getArrayNItems() <= 5,
                "Invalid PDF form procedure resources.");
            for (int i = 0; i < value.getArrayNItems(); ++i) {
                auto item = value.getArrayItem(i);
                Require(item.isNameAndEquals("/PDF") || item.isNameAndEquals("/Text") ||
                    item.isNameAndEquals("/ImageB") || item.isNameAndEquals("/ImageC") ||
                    item.isNameAndEquals("/ImageI"), "Invalid PDF form procedure resource.");
            }
        } else Unsupported("PDF form resource kind is unsupported.");
    }
}

struct StructureBaseline {
    std::string semantics;
    size_t orphanMcids = 0;
    size_t missingParentLinks = 0;
    size_t mismatchedParentLinks = 0;
    size_t detachedArtifacts = 0;
};

class StructureSnapshot final {
  public:
    explicit StructureSnapshot(QPDF& pdf,
        std::map<size_t, std::string> const* markerContents = nullptr) :
        _pdf(pdf), _markerContents(markerContents)
    {
        auto pages = pdf.getAllPages();
        RequireLimit(pages.size() <= MaximumPages, "PDF structure page budget exceeded.");
        for (size_t index = 0; index < pages.size(); ++index) {
            Require(pages[index].isIndirect(), "PDF page has no stable structure identity.");
            _pages.emplace(pages[index].getObjGen(), index);
            auto annots = pages[index].getKey("/Annots");
            Require(annots.isNull() || annots.isArray(), "Invalid PDF annotation array.");
            if (annots.isNull()) continue;
            RequireLimit(annots.getArrayNItems() <= 4096, "PDF annotation budget exceeded.");
            for (int item = 0; item < annots.getArrayNItems(); ++item) {
                auto annot = annots.getArrayItem(item);
                Require(annot.isDictionary(), "Invalid PDF annotation reference.");
                if (!annot.isIndirect()) {
                    // Direct annotation entries were allowed before PDF 1.7. They have
                    // no object identity, so the positional snapshot compares their
                    // complete dictionaries instead of adding them to _annotations.
                    auto version = _pdf.getPDFVersion();
                    auto catalogVersion = _pdf.getRoot().getKey("/Version");
                    Require((version == "1.0" || version == "1.1" || version == "1.2" ||
                        version == "1.3" || version == "1.4" || version == "1.5" ||
                        version == "1.6") &&
                        (catalogVersion.isNull() || catalogVersion.isNameAndEquals("/1.0") ||
                        catalogVersion.isNameAndEquals("/1.1") ||
                        catalogVersion.isNameAndEquals("/1.2") ||
                        catalogVersion.isNameAndEquals("/1.3") ||
                        catalogVersion.isNameAndEquals("/1.4") ||
                        catalogVersion.isNameAndEquals("/1.5") ||
                        catalogVersion.isNameAndEquals("/1.6")),
                        "Direct PDF annotations require a pre-1.7 document.");
                    continue;
                }
                auto token = std::to_string(index) + ":" + std::to_string(item);
                Require(_annotations.emplace(annot.getObjGen(), token).second,
                    "PDF annotation is reused across pages.");
            }
        }
    }

    StructureBaseline Capture(bool allowDecorativePrefix = false)
    {
        auto root = _pdf.getRoot();
        ValidatePassiveAcroForm(_pdf);
        for (auto key : {"/AA", "/Collection", "/Perms"})
            if (!root.getKey(key).isNull()) Unsupported("Active PDF catalog feature is unsupported.");
        auto names = root.getKey("/Names");
        if (!names.isNull()) {
            Require(names.isDictionary(), "Invalid PDF name tree.");
            for (auto const& key : names.getKeys())
                if (key != "/EmbeddedFiles" && key != "/Dests")
                    Unsupported("Active PDF name tree is unsupported.");
        }
        ReadDestinations(root);
        auto uriBase = root.getKey("/URI");
        if (!uriBase.isNull()) {
            Require(uriBase.isDictionary() && uriBase.getKeys() == std::set<std::string>{"/Base"},
                "Invalid PDF URI base.");
            ValidateUri(uriBase.getKey("/Base"), false);
        }
        auto open = root.getKey("/OpenAction");
        if (!open.isNull()) {
            if (open.isDictionary()) ValidateGoToAction(open);
            else ValidateDestination(open);
        }
        ValidateLayers(root.getKey("/OCProperties"));
        ValidateAnnotations();
        ValidateTags(root.getKey("/StructTreeRoot"), root.getKey("/MarkInfo"));
        ValidateOutlines(root.getKey("/Outlines"));
        for (auto key : {"/StructTreeRoot", "/MarkInfo", "/Outlines", "/Names",
            "/OCProperties", "/Lang", "/ViewerPreferences", "/Dests", "/PageMode", "/PageLayout",
            "/AcroForm", "/OpenAction", "/URI"}) {
            Add(_result, key);
            Encode(root.getKey(key), _result, 0);
        }
        auto pages = _pdf.getAllPages();
        size_t pageIndex = 0;
        for (auto page : pages) {
            Encode(page.getKey("/StructParents"), _result, 0);
            Encode(page.getKey("/Tabs"), _result, 0);
            auto annots = page.getKey("/Annots");
            Encode(annots, _result, 0);
            if (annots.isArray())
                for (int i = 0; i < annots.getArrayNItems(); ++i)
                    Encode(annots.getArrayItem(i), _result, 0, true);
            QPDFPageObjectHelper helper(page);
            auto resources = helper.getAttribute("/Resources", false);
            ValidatePageLayers(resources);
            Encode(resources.getKey("/Properties"), _result, 0);
            Markers markers(resources, allowDecorativePrefix);
            // The output verifier supplies this only after proving the exact
            // generated stream and removing its planned background and local rectangles.
            // ValidateTags above still inspects the complete, unfiltered PDF.
            if (_markerContents && _markerContents->count(pageIndex))
                QPDFObjectHandle::parseContentStream(
                    QPDFObjectHandle::newStream(&_pdf, _markerContents->at(pageIndex)), &markers);
            else helper.parsePageContents(&markers);
            Add(_result, markers.result);
            ++pageIndex;
        }
        RequireLimit(_result.size() <= MaximumSourceBytes * 2,
            "PDF structure snapshot budget exceeded.");
        return {_result, _orphanMcids, _missingParentLinks,
            _mismatchedParentLinks, _detachedArtifacts};
    }

  private:
    static void Add(std::string& output, std::string const& value)
    {
        RequireLimit(value.size() <= MaximumSourceBytes &&
            output.size() <= MaximumSourceBytes * 2 - value.size() - 32,
            "PDF structure snapshot budget exceeded.");
        output += std::to_string(value.size()) + ":" + value;
    }

    // Object numbers are writer-specific. Traversal labels retain aliasing and
    // back-references while page and annotation references use semantic positions.
    void Encode(QPDFObjectHandle object, std::string& output, unsigned depth,
        bool expandAnnotation = false)
    {
        RequireLimit(depth <= 512, "PDF structure nesting budget exceeded.");
        // Writers may resolve an indirect null to a direct null. It carries no
        // object identity or passive payload; compare its value before aliases.
        if (object.isNull()) { Add(output, "null"); return; }
        if (object.isIndirect()) {
            auto id = object.getObjGen();
            auto page = _pages.find(id);
            if (page != _pages.end()) {
                Add(output, "page:" + std::to_string(page->second));
                return;
            }
            auto annot = _annotations.find(id);
            if (annot != _annotations.end() && !expandAnnotation) {
                Add(output, "annotation:" + annot->second);
                return;
            }
            auto found = _visited.find(id);
            if (found != _visited.end()) {
                Add(output, "reference:" + std::to_string(found->second));
                return;
            }
            RequireLimit(_visited.size() < MaximumObjects,
                "PDF structure object budget exceeded.");
            auto label = _visited.size();
            _visited.emplace(id, label);
            Add(output, "object:" + std::to_string(label));
        }
        if (object.isArray()) {
            RequireLimit(object.getArrayNItems() <= static_cast<int>(MaximumObjects),
                "PDF structure array budget exceeded.");
            Add(output, "array:" + std::to_string(object.getArrayNItems()));
            for (int i = 0; i < object.getArrayNItems(); ++i)
                Encode(object.getArrayItem(i), output, depth + 1);
            return;
        }
        if (object.isDictionary() || object.isStream()) {
            auto dict = object.isStream() ? object.getDict() : object;
            Add(output, object.isStream() ? "stream" : "dictionary");
            for (auto const& key : dict.getKeys()) {
                // Compression is a transport detail; decoded attachment and
                // appearance bytes below are the preserved payload.
                if (object.isStream() && (key == "/Length" || key == "/Filter" ||
                    key == "/DecodeParms")) continue;
                Add(output, key);
                Encode(dict.getKey(key), output, depth + 1);
            }
            if (object.isStream()) {
                auto decoded = object.getStreamData(qpdf_dl_all);
                RequireLimit(decoded->getSize() <= MaximumSourceBytes,
                    "PDF passive stream budget exceeded.");
                Add(output, std::string(reinterpret_cast<char const*>(decoded->getBuffer()),
                    decoded->getSize()));
            }
            return;
        }
        Require(object.isScalar(), "Invalid PDF structure object.");
        Add(output, object.unparse());
    }

    class Markers final : public QPDFObjectHandle::ParserCallbacks {
      public:
        explicit Markers(QPDFObjectHandle resources, bool allowDecorativePrefix = false) :
            _resources(resources), _allowDecorativePrefix(allowDecorativePrefix) {}
        void handleObject(QPDFObjectHandle object) override
        {
            if (!object.isOperator()) {
                _operands.push_back(object);
                RequireLimit(_operands.size() <= 256, "PDF marked-content operand budget exceeded.");
                return;
            }
            auto op = object.getOperatorValue();
            if (op == "BDC" || op == "BMC") {
                Require(_operands.size() == (op == "BDC" ? 2 : 1) && _operands[0].isName(),
                    "Invalid PDF marked-content opener.");
                RequireLimit(++_depth <= 64, "PDF marked-content nesting budget exceeded.");
                if (_allowDecorativePrefix && !_seenMarker && op == "BMC" &&
                    _operands[0].isNameAndEquals("/Artifact")) {
                    _skipArtifact = true;
                    _seenMarker = true;
                    _operands.clear();
                    return;
                }
                Require(!_skipArtifact, "Decorative PDF artifact contains marked content.");
                _seenMarker = true;
                Add(result, op);
                Add(result, _operands[0].getName());
                if (op == "BDC") {
                    auto properties = _operands[1];
                    if (properties.isName()) {
                        auto names = _resources.getKey("/Properties");
                        Require(names.isDictionary() && names.hasKey(properties.getName()),
                            "PDF marked-content property is missing.");
                        Add(result, properties.getName());
                        properties = names.getKey(properties.getName());
                    }
                    Require(properties.isDictionary(), "Invalid PDF marked-content property.");
                    auto mcid = properties.getKey("/MCID");
                    if (!mcid.isNull()) {
                        Require(mcid.isInteger() && mcid.getIntValue() >= 0 &&
                            mcid.getIntValue() < 65536, "Invalid PDF marked-content identifier.");
                        Require(_mcids.insert(static_cast<int>(mcid.getIntValue())).second,
                            "Duplicate PDF marked-content identifier on a page.");
                    }
                    AddProperty(result, properties, 0);
                }
            } else if (op == "EMC") {
                Require(_operands.empty() && _depth > 0, "Unbalanced PDF marked content.");
                --_depth;
                if (_skipArtifact) {
                    Require(_depth == 0, "Decorative PDF artifact is nested.");
                    _skipArtifact = false;
                } else Add(result, op);
            }
            _operands.clear();
            RequireLimit(result.size() <= MaximumContentBytes,
                "PDF marked-content snapshot budget exceeded.");
        }
        void handleEOF() override { Require(_depth == 0, "Unclosed PDF marked content."); }
        std::set<int> const& Mcids() const { return _mcids; }
        std::string result;

      private:
        static void AddProperty(std::string& output, QPDFObjectHandle value,
            unsigned depth)
        {
            RequireLimit(depth < 16, "PDF marked-content property depth exceeded.");
            if (value.isDictionary()) {
                Add(output, "dictionary");
                for (auto const& key : value.getKeys()) {
                    Add(output, key);
                    AddProperty(output, value.getKey(key), depth + 1);
                }
            } else if (value.isArray()) {
                Add(output, "array:" + std::to_string(value.getArrayNItems()));
                for (int i = 0; i < value.getArrayNItems(); ++i)
                    AddProperty(output, value.getArrayItem(i), depth + 1);
            } else {
                Require(value.isScalar(), "Invalid PDF marked-content property value.");
                Add(output, value.unparse());
            }
        }
        QPDFObjectHandle _resources;
        std::vector<QPDFObjectHandle> _operands;
        std::set<int> _mcids;
        unsigned _depth = 0;
        bool _allowDecorativePrefix = false;
        bool _seenMarker = false;
        bool _skipArtifact = false;
    };

    void ValidateLayers(QPDFObjectHandle properties)
    {
        if (properties.isNull()) return;
        Require(properties.isDictionary(), "Invalid PDF layer catalog.");
        for (auto const& key : properties.getKeys())
            if (key != "/OCGs" && key != "/D")
                Unsupported("Conditional PDF layer configuration is unsupported.");
        auto groups = properties.getKey("/OCGs"), config = properties.getKey("/D");
        Require(groups.isArray() && config.isDictionary(), "Invalid PDF layer configuration.");
        RequireLimit(groups.getArrayNItems() <= 256, "PDF layer budget exceeded.");
        std::set<QPDFObjGen> ids;
        for (int i = 0; i < groups.getArrayNItems(); ++i) {
            auto group = groups.getArrayItem(i);
            Require(group.isIndirect() && group.isDictionary() &&
                group.getKey("/Type").isNameAndEquals("/OCG") &&
                group.getKey("/Name").isString(), "Invalid PDF optional-content group.");
            Require(ids.insert(group.getObjGen()).second, "Duplicate PDF optional-content group.");
            _layerIds.insert(group.getObjGen());
            auto usage = group.getKey("/Usage");
            if (!usage.isNull()) {
                Require(usage.isDictionary(), "Invalid PDF layer usage.");
                for (auto const& key : usage.getKeys()) {
                    if (key != "/PageElement" && key != "/View")
                        Unsupported("Conditional PDF layer usage is unsupported.");
                    auto entry = usage.getKey(key);
                    Require(entry.isDictionary(), "Invalid PDF layer usage.");
                    if (key == "/PageElement")
                        Require(entry.getKeys() == std::set<std::string>{"/Subtype"} &&
                            entry.getKey("/Subtype").isNameAndEquals("/BG"),
                            "Unsupported PDF page-element layer usage.");
                    else Require(entry.getKeys() == std::set<std::string>{"/ViewState"} &&
                        (entry.getKey("/ViewState").isNameAndEquals("/ON") ||
                         entry.getKey("/ViewState").isNameAndEquals("/OFF")),
                        "Unsupported PDF view-layer usage.");
                }
            }
        }
        for (auto const& key : config.getKeys())
            if (key != "/Order" && key != "/RBGroups" && key != "/ON" &&
                key != "/OFF" && key != "/BaseState" && key != "/Name")
                Unsupported("Conditional PDF layer configuration is unsupported.");
        auto radio = config.getKey("/RBGroups");
        if (!radio.isNull())
            Require(radio.isArray() && radio.getArrayNItems() == 0,
                "PDF layer radio groups are unsupported.");
        auto base = config.getKey("/BaseState");
        if (!base.isNull() && !(base.isNameAndEquals("/ON") || base.isNameAndEquals("/OFF")))
            Unsupported("PDF layer base state is unsupported.");
        for (auto key : {"/ON", "/OFF", "/Order"}) {
            auto list = config.getKey(key);
            if (list.isNull()) continue;
            Require(list.isArray(), "Invalid PDF layer list.");
            for (int i = 0; i < list.getArrayNItems(); ++i) {
                auto item = list.getArrayItem(i);
                Require(item.isIndirect() && ids.count(item.getObjGen()),
                    "PDF layer list references an unknown group.");
            }
        }
    }

    void ValidatePageLayers(QPDFObjectHandle resources)
    {
        auto properties = resources.getKey("/Properties");
        if (properties.isNull()) return;
        Require(properties.isDictionary(), "Invalid PDF marked-content properties.");
        for (auto const& name : properties.getKeys()) {
            auto value = properties.getKey(name);
            Require(value.isDictionary(), "Invalid PDF marked-content property.");
            if (value.getKey("/Type").isNameAndEquals("/OCMD"))
                Unsupported("Conditional PDF layer membership is unsupported.");
            if (value.getKey("/Type").isNameAndEquals("/OCG"))
                Require(value.isIndirect() && _layerIds.count(value.getObjGen()),
                    "PDF marked content uses an unknown layer.");
        }
    }

    void ValidateGoToAction(QPDFObjectHandle action)
    {
        Require(action.isDictionary(), "Invalid PDF navigation action.");
        if (!action.getKey("/S").isNameAndEquals("/GoTo"))
            Unsupported("Active PDF navigation action is unsupported.");
        for (auto const& key : action.getKeys())
            if (key != "/Type" && key != "/S" && key != "/D")
                Unsupported("Active PDF navigation action is unsupported.");
        auto type = action.getKey("/Type");
        Require(type.isNull() || type.isNameAndEquals("/Action"),
            "Invalid PDF navigation action type.");
        ValidateDestination(action.getKey("/D"));
    }

    void ValidateUri(QPDFObjectHandle uri, bool allowRelative = true)
    {
        Require(uri.isString(), "Invalid PDF URI link.");
        auto value = uri.getStringValue();
        RequireLimit(!value.empty() && value.size() <= 65536, "PDF URI length exceeds its bound.");
        std::string lower;
        for (unsigned char character : value) {
            Require(character > 32 && character < 127 && character != '<' && character != '>' &&
                character != '"' && character != '\\', "Invalid PDF URI characters.");
            lower += character >= 'A' && character <= 'Z' ? character + ('a' - 'A') : character;
        }
        if (lower.rfind("http://", 0) == 0 || lower.rfind("https://", 0) == 0 ||
            allowRelative && lower.rfind("mailto:", 0) == 0) return;
        // Retain ordinary relative/schemeless references as inert annotation data.
        // No URL is opened, normalized, given a scheme, or resolved against /Base.
        if (allowRelative && lower.find(':') == std::string::npos &&
            (lower.find('.') != std::string::npos || lower.find('/') != std::string::npos ||
             lower.front() == '#')) return;
        Unsupported("PDF URI scheme is unsupported.");
    }

    void ReadDestinations(QPDFObjectHandle root)
    {
        auto legacy = root.getKey("/Dests");
        if (!legacy.isNull()) {
            Require(legacy.isDictionary(), "Invalid PDF destination dictionary.");
            RequireLimit(legacy.getKeys().size() <= 4096, "PDF destination budget exceeded.");
            for (auto const& key : legacy.getKeys()) {
                auto value = legacy.getKey(key);
                ValidateExplicitDestination(value);
                _legacyDestinations.emplace(key.substr(1), value);
            }
        }
        auto names = root.getKey("/Names");
        auto tree = names.isDictionary() ? names.getKey("/Dests") : QPDFObjectHandle::newNull();
        if (!tree.isNull()) ReadDestinationTree(tree, 0);
    }

    std::pair<std::string, std::string> ReadDestinationTree(QPDFObjectHandle tree, unsigned depth)
    {
        RequireLimit(depth <= 32 && ++_destinationNodes <= 4096,
            "PDF destination tree budget exceeded.");
        Require(tree.isDictionary(), "Invalid PDF destination name tree.");
        if (tree.isIndirect()) Require(_destinationTrees.insert(tree.getObjGen()).second,
            "Cyclic or reused PDF destination tree.");
        for (auto const& key : tree.getKeys())
            Require(key == "/Names" || key == "/Kids" || key == "/Limits",
                "Invalid PDF destination tree key.");
        auto values = tree.getKey("/Names"), kids = tree.getKey("/Kids");
        Require(values.isNull() != kids.isNull(), "Invalid PDF destination tree branches.");
        std::pair<std::string, std::string> range;
        bool first = true;
        if (!values.isNull()) {
            Require(values.isArray() && values.getArrayNItems() % 2 == 0,
                "Invalid PDF destination names.");
            RequireLimit(values.getArrayNItems() <= 8192, "PDF destination budget exceeded.");
            for (int i = 0; i < values.getArrayNItems(); i += 2) {
                auto key = values.getArrayItem(i), value = values.getArrayItem(i + 1);
                Require(key.isString(), "Invalid PDF destination name.");
                auto text = key.getStringValue();
                RequireLimit(!text.empty() && text.size() <= 65536 &&
                    _stringDestinations.size() < 4096, "PDF destination budget exceeded.");
                Require(first || range.second < text, "Unsorted or duplicate PDF destination names.");
                ValidateExplicitDestination(value);
                Require(_stringDestinations.emplace(text, value).second,
                    "Duplicate PDF destination name.");
                if (first) range.first = text;
                range.second = text;
                first = false;
            }
        } else {
            Require(kids.isArray() && kids.getArrayNItems() > 0 && kids.getArrayNItems() <= 4096,
                "Invalid PDF destination tree children.");
            for (int i = 0; i < kids.getArrayNItems(); ++i) {
                auto child = ReadDestinationTree(kids.getArrayItem(i), depth + 1);
                Require(first || range.second < child.first,
                    "Overlapping PDF destination tree ranges.");
                if (first) range.first = child.first;
                range.second = child.second;
                first = false;
            }
        }
        Require(!first || depth == 0, "Empty PDF destination tree child.");
        auto limits = tree.getKey("/Limits");
        if (!limits.isNull() || depth != 0) {
            Require(!first && limits.isArray() && limits.getArrayNItems() == 2 &&
                limits.getArrayItem(0).isString() && limits.getArrayItem(1).isString() &&
                limits.getArrayItem(0).getStringValue() == range.first &&
                limits.getArrayItem(1).getStringValue() == range.second,
                "Invalid PDF destination tree limits.");
        }
        return range;
    }

    void ValidateOutlines(QPDFObjectHandle outlines)
    {
        if (outlines.isNull()) return;
        Require(outlines.isDictionary(), "Invalid PDF outlines.");
        auto item = outlines.getKey("/First");
        std::set<QPDFObjGen> seen;
        while (!item.isNull()) {
            RequireLimit(seen.size() < MaximumObjects, "PDF outline budget exceeded.");
            Require(item.isIndirect() && item.isDictionary() &&
                seen.insert(item.getObjGen()).second, "Invalid PDF outline chain.");
            auto action = item.getKey("/A");
            if (!action.isNull()) {
                ValidateGoToAction(action);
            }
            if (!item.getKey("/Dest").isNull())
                ValidateDestination(item.getKey("/Dest"));
            auto child = item.getKey("/First");
            if (!child.isNull()) ValidateOutlineChildren(child, seen, 0);
            item = item.getKey("/Next");
        }
    }

    void ValidateOutlineChildren(QPDFObjectHandle item, std::set<QPDFObjGen>& seen,
        unsigned depth)
    {
        RequireLimit(depth < 64, "PDF outline depth exceeded.");
        while (!item.isNull()) {
            RequireLimit(seen.size() < MaximumObjects, "PDF outline budget exceeded.");
            Require(item.isIndirect() && item.isDictionary() &&
                seen.insert(item.getObjGen()).second, "Invalid PDF outline chain.");
            auto action = item.getKey("/A");
            if (!action.isNull()) ValidateGoToAction(action);
            if (!item.getKey("/Dest").isNull())
                ValidateDestination(item.getKey("/Dest"));
            auto child = item.getKey("/First");
            if (!child.isNull()) ValidateOutlineChildren(child, seen, depth + 1);
            item = item.getKey("/Next");
        }
    }

    void ValidateAnnotations()
    {
        auto pages = _pdf.getAllPages();
        for (auto page : pages) {
            auto annots = page.getKey("/Annots");
            if (annots.isNull()) continue;
            for (int i = 0; i < annots.getArrayNItems(); ++i) {
                auto annot = annots.getArrayItem(i);
                for (auto key : {"/AA", "/RichMediaContent", "/RichMediaSettings", "/Sound"})
                    if (!annot.getKey(key).isNull())
                        Unsupported("Active PDF annotation is unsupported.");
                auto subtype = annot.getKey("/Subtype");
                if (subtype.isNameAndEquals("/Link")) {
                    auto action = annot.getKey("/A"), dest = annot.getKey("/Dest");
                    Require(action.isNull() != dest.isNull(), "Invalid PDF link destination.");
                    if (!action.isNull()) {
                        Require(action.isDictionary() && action.getKey("/Next").isNull(),
                            "Invalid PDF link action.");
                        auto kind = action.getKey("/S");
                        if (kind.isNameAndEquals("/URI")) {
                            for (auto const& key : action.getKeys())
                                if (key != "/S" && key != "/URI" && key != "/IsMap" && key != "/Type")
                                    Unsupported("Active PDF link action is unsupported.");
                            auto type = action.getKey("/Type"), map = action.getKey("/IsMap");
                            Require(type.isNull() || type.isNameAndEquals("/Action"),
                                "Invalid PDF URI action type.");
                            Require(map.isNull() || map.isBool(), "Invalid PDF URI map flag.");
                            ValidateUri(action.getKey("/URI"));
                        } else if (kind.isNameAndEquals("/GoTo")) {
                            ValidateGoToAction(action);
                        } else Unsupported("Active PDF link action is unsupported.");
                    } else ValidateDestination(dest);
                } else if (subtype.isNameAndEquals("/FileAttachment")) {
                    Require(annot.getKey("/A").isNull(), "Active PDF attachment is unsupported.");
                    auto file = annot.getKey("/FS");
                    Require(file.isDictionary() && file.getKey("/Type").isNameAndEquals("/Filespec"),
                        "Invalid PDF attachment file specification.");
                    auto embedded = file.getKey("/EF");
                    Require(embedded.isDictionary() && embedded.getKey("/F").isStream(),
                        "PDF attachment payload is missing.");
                } else Unsupported("PDF annotation subtype is unsupported.");
                auto rect = annot.getKey("/Rect");
                Require(rect.isArray() && rect.getArrayNItems() == 4,
                    "Invalid PDF annotation rectangle.");
                std::array<double, 4> box{};
                for (int coordinate = 0; coordinate < 4; ++coordinate) {
                    auto value = rect.getArrayItem(coordinate);
                    Require(value.isNumber(), "Invalid PDF annotation rectangle.");
                    box[coordinate] = value.getNumericValue();
                    Require(std::isfinite(box[coordinate]) && std::abs(box[coordinate]) <= 10000000,
                        "PDF annotation rectangle exceeds its bound.");
                }
                Require(box[0] <= box[2] && box[1] <= box[3],
                    "Invalid PDF annotation rectangle.");
            }
        }
    }

    void ValidateDestination(QPDFObjectHandle destination)
    {
        if (destination.isName() || destination.isString()) {
            auto& names = destination.isName() ? _legacyDestinations : _stringDestinations;
            auto key = destination.isName() ? destination.getName().substr(1) :
                destination.getStringValue();
            auto found = names.find(key);
            Require(found != names.end(), "Named PDF destination is missing.");
            destination = found->second;
        }
        ValidateExplicitDestination(destination);
    }

    void ValidateExplicitDestination(QPDFObjectHandle destination)
    {
        if (destination.isDictionary()) {
            Require(destination.getKeys() == std::set<std::string>{"/D"},
                "Invalid PDF named destination dictionary.");
            destination = destination.getKey("/D");
        }
        Require(destination.isArray() && destination.getArrayNItems() >= 2 &&
            destination.getArrayNItems() <= 6, "Invalid PDF internal destination.");
        auto target = destination.getArrayItem(0);
        Require(target.isIndirect() && _pages.count(target.getObjGen()) &&
            destination.getArrayItem(1).isName(),
            "PDF internal destination does not target a page.");
        auto fit = destination.getArrayItem(1).getName();
        int count = fit == "/XYZ" ? 5 : fit == "/FitR" ? 6 :
            fit == "/FitH" || fit == "/FitV" || fit == "/FitBH" || fit == "/FitBV" ? 3 :
            fit == "/Fit" || fit == "/FitB" ? 2 : 0;
        Require(count && destination.getArrayNItems() == count,
            "Invalid PDF destination fit parameters.");
        for (int i = 2; i < count; ++i) {
            auto value = destination.getArrayItem(i);
            if (value.isNull() && fit != "/FitR") continue;
            Require(value.isNumber() && std::isfinite(value.getNumericValue()) &&
                std::abs(value.getNumericValue()) <= 10000000 &&
                (fit != "/XYZ" || i != 4 || value.getNumericValue() >= 0),
                "Invalid PDF destination coordinate.");
        }
        if (fit == "/FitR")
            Require(destination.getArrayItem(2).getNumericValue() < destination.getArrayItem(4).getNumericValue() &&
                destination.getArrayItem(3).getNumericValue() < destination.getArrayItem(5).getNumericValue(),
                "Invalid PDF destination rectangle.");
    }

    void ValidateTags(QPDFObjectHandle tree, QPDFObjectHandle markInfo)
    {
        auto pages = _pdf.getAllPages();
        for (auto page : pages) {
            QPDFPageObjectHelper helper(page);
            ValidateForms(helper.getAttribute("/Resources", false), 0);
        }
        if (tree.isNull()) {
            if (markInfo.isDictionary() && markInfo.getKey("/Marked").isBool() &&
                markInfo.getKey("/Marked").getBoolValue())
                Require(false, "Marked PDF has no structure tree.");
            return;
        }
        Require(tree.isDictionary() && tree.getKey("/Type").isNameAndEquals("/StructTreeRoot"),
            "Invalid PDF structure root.");
        Require(markInfo.isDictionary() && markInfo.getKey("/Marked").isBool() &&
            markInfo.getKey("/Marked").getBoolValue(), "PDF structure is not marked.");
        auto parentTree = tree.getKey("/ParentTree");
        Require(parentTree.isDictionary(), "PDF structure parent tree is missing.");
        ReadParentTree(parentTree, 0);
        for (size_t index = 0; index < pages.size(); ++index) {
            QPDFPageObjectHelper helper(pages[index]);
            auto resources = helper.getAttribute("/Resources", false);
            Markers markers(resources);
            helper.parsePageContents(&markers);
            _pageMcids.emplace(pages[index].getObjGen(), markers.Mcids());
            auto indexObject = pages[index].getKey("/StructParents");
            if (markers.Mcids().empty()) continue;
            Require(indexObject.isInteger() && indexObject.getIntValue() >= 0 &&
                indexObject.getIntValue() <= 65535, "Tagged PDF page has no parent index.");
            auto found = _parentTree.find(static_cast<int>(indexObject.getIntValue()));
            Require(found != _parentTree.end() && found->second.isArray(),
                "Tagged PDF page parent array is missing.");
            for (int mcid : markers.Mcids()) {
                if (mcid >= found->second.getArrayNItems()) continue;
                Require(found->second.getArrayItem(mcid).isIndirect() &&
                    found->second.getArrayItem(mcid).isDictionary(),
                    "Tagged PDF content has no structure parent.");
            }
        }
        WalkKids(tree.getKey("/K"), tree, QPDFObjectHandle(), 0);
        for (auto const& entry : _pageMcids) {
            if (entry.second.empty()) continue;
            auto page = _pdf.getObjectByObjGen(entry.first);
            auto index = page.getKey("/StructParents");
            auto parents = _parentTree.at(static_cast<int>(index.getIntValue()));
            for (int mcid : entry.second) {
                if (mcid >= parents.getArrayNItems()) {
                    Require(_referencedMcids.count({entry.first, mcid}),
                        "PDF marked content has neither a parent nor a tree reference.");
                    continue;
                }
                auto mapped = parents.getArrayItem(mcid);
                Require(mapped.isIndirect() && mapped.isDictionary(),
                    "PDF marked content maps to an invalid structure element.");
                if (!_elements.count(mapped.getObjGen())) {
                    Require(mapped.getKey("/S").isNameAndEquals("/Artifact") &&
                        IsPassiveArtifactParent(mapped, entry.first) &&
                        mapped.getKey("/K").isInteger() &&
                        mapped.getKey("/K").getIntValue() == mcid &&
                        mapped.getKey("/Pg").isIndirect() &&
                        mapped.getKey("/Pg").getObjGen() == entry.first &&
                        !_referencedMcids.count({entry.first, mcid}),
                        "PDF marked content maps to an unreachable structure element.");
                    RequireLimit(++_detachedArtifacts <= 64,
                        "PDF detached artifact budget exceeded.");
                    continue;
                }
                if (!_referencedMcids.count({entry.first, mcid}))
                    RequireLimit(++_orphanMcids <= 64,
                        "PDF source has too many unreferenced marked regions.");
            }
        }
    }

    bool IsPassiveArtifactParent(QPDFObjectHandle artifact, QPDFObjGen const& page)
    {
        auto parent = artifact.getKey("/P");
        if (parent.isNull()) return true;
        // Some presentation exporters retain an Artifact only in ParentTree,
        // pointing back to its reachable Slide without listing it in Slide/K.
        // Preserve that graph verbatim; do not infer decorative paint or repair
        // the missing forward edge. Integer K is checked by the caller, so this
        // exception cannot introduce a second structure subtree or a cycle.
        std::set<std::string> keys{"/Type", "/S", "/P", "/Pg", "/K"};
        return artifact.getKeys() == keys &&
            artifact.getKey("/Type").isNameAndEquals("/StructElem") &&
            parent.isIndirect() && parent.isDictionary() &&
            _elements.count(parent.getObjGen()) && parent.getKeys() == keys &&
            parent.getKey("/Type").isNameAndEquals("/StructElem") &&
            parent.getKey("/S").isNameAndEquals("/Slide") &&
            parent.getKey("/Pg").isIndirect() && parent.getKey("/Pg").getObjGen() == page;
    }

    void ValidateForms(QPDFObjectHandle resources, unsigned depth)
    {
        RequireLimit(depth < MaximumDepth, "PDF form structure nesting exceeded.");
        auto xobjects = resources.getKey("/XObject");
        if (xobjects.isNull()) return;
        Require(xobjects.isDictionary(), "Invalid PDF XObject resources.");
        for (auto const& name : xobjects.getKeys()) {
            auto object = xobjects.getKey(name);
            if (!object.isStream()) continue;
            auto dict = object.getDict();
            auto layer = dict.getKey("/OC");
            if (!layer.isNull())
                Require(layer.isIndirect() && _layerIds.count(layer.getObjGen()),
                    "PDF XObject uses an unsupported layer membership.");
            if (!dict.getKey("/Subtype").isNameAndEquals("/Form")) continue;
            if (!dict.getKey("/StructParents").isNull() ||
                !dict.getKey("/StructParent").isNull())
                Unsupported("Tagged form streams need structure remapping.");
            auto nested = dict.getKey("/Resources");
            if (!nested.isNull()) ValidateForms(nested, depth + 1);
        }
    }

    void ReadParentTree(QPDFObjectHandle node, unsigned depth)
    {
        RequireLimit(depth < 32, "PDF parent tree depth exceeded.");
        auto nums = node.getKey("/Nums"), kids = node.getKey("/Kids");
        Require(nums.isNull() != kids.isNull(), "Invalid PDF parent tree node.");
        if (nums.isArray()) {
            Require(nums.getArrayNItems() % 2 == 0, "Invalid PDF parent tree entries.");
            for (int i = 0; i < nums.getArrayNItems(); i += 2) {
                auto key = nums.getArrayItem(i);
                Require(key.isInteger() && key.getIntValue() >= 0 && key.getIntValue() <= 65535,
                    "Invalid PDF parent tree index.");
                Require(_parentTree.emplace(static_cast<int>(key.getIntValue()),
                    nums.getArrayItem(i + 1)).second, "Duplicate PDF parent tree index.");
            }
        } else {
            Require(kids.isArray(), "Invalid PDF parent tree children.");
            for (int i = 0; i < kids.getArrayNItems(); ++i)
                ReadParentTree(kids.getArrayItem(i), depth + 1);
        }
        RequireLimit(_parentTree.size() <= MaximumObjects, "PDF parent tree budget exceeded.");
    }

    void WalkKids(QPDFObjectHandle kids, QPDFObjectHandle parent, QPDFObjectHandle page,
        unsigned depth)
    {
        RequireLimit(depth <= 64 && ++_kidCount <= MaximumObjects * 4,
            "PDF structure tree budget exceeded.");
        if (kids.isNull()) return;
        if (kids.isArray()) {
            for (int i = 0; i < kids.getArrayNItems(); ++i)
                WalkKids(kids.getArrayItem(i), parent, page, depth + 1);
            return;
        }
        if (kids.isInteger() || kids.isDictionary() && !kids.getKey("/MCID").isNull()) {
            QPDFObjectHandle mcid = kids;
            if (kids.isDictionary()) {
                if (!kids.getKey("/Stm").isNull())
                    Unsupported("Tagged form streams need structure remapping.");
                mcid = kids.getKey("/MCID");
                if (!kids.getKey("/Pg").isNull()) page = kids.getKey("/Pg");
            }
            Require(mcid.isInteger() && mcid.getIntValue() >= 0 &&
                mcid.getIntValue() < 65536 && page.isIndirect() &&
                _pages.count(page.getObjGen()), "Invalid PDF marked-content reference.");
            auto actual = _pageMcids.find(page.getObjGen());
            Require(actual != _pageMcids.end() &&
                actual->second.count(static_cast<int>(mcid.getIntValue())),
                "PDF structure references missing marked content.");
            Require(_referencedMcids.insert({page.getObjGen(),
                static_cast<int>(mcid.getIntValue())}).second,
                "PDF marked content has duplicate structure references.");
            auto index = page.getKey("/StructParents");
            Require(index.isInteger(), "Tagged PDF page parent index is missing.");
            auto found = _parentTree.find(static_cast<int>(index.getIntValue()));
            Require(found != _parentTree.end() && found->second.isArray(),
                "PDF MCID parent array is missing.");
            if (mcid.getIntValue() >= found->second.getArrayNItems()) {
                RequireLimit(++_missingParentLinks <= 64,
                    "PDF source has too many missing MCID parent links.");
            } else {
                auto mapped = found->second.getArrayItem(static_cast<int>(mcid.getIntValue()));
                Require(mapped.isIndirect() && mapped.isDictionary(),
                    "PDF MCID parent relationship is broken.");
                if (mapped.getObjGen() != parent.getObjGen())
                    RequireLimit(++_mismatchedParentLinks <= 256,
                        "PDF source has too many mismatched MCID parent links.");
            }
            return;
        }
        if (kids.isDictionaryOfType("/OBJR")) {
            auto target = kids.getKey("/Obj");
            Require(target.isIndirect() && _annotations.count(target.getObjGen()),
                "PDF object reference does not target an annotation.");
            auto index = target.getKey("/StructParent");
            Require(index.isInteger(), "Tagged PDF annotation parent index is missing.");
            auto found = _parentTree.find(static_cast<int>(index.getIntValue()));
            Require(found != _parentTree.end() && found->second.isIndirect() &&
                found->second.getObjGen() == parent.getObjGen(),
                "PDF annotation parent relationship is broken.");
            return;
        }
        Require(kids.isIndirect() && kids.isDictionary() &&
            (kids.getKey("/Type").isNull() ||
             kids.getKey("/Type").isNameAndEquals("/StructElem")) &&
            kids.getKey("/S").isName(), "Invalid PDF structure element.");
        Require(kids.getKey("/P").isIndirect() &&
            kids.getKey("/P").getObjGen() == parent.getObjGen(),
            "PDF structure parent relationship is broken.");
        Require(_elements.insert(kids.getObjGen()).second,
            "Cyclic or reused PDF structure element.");
        if (!kids.getKey("/Pg").isNull()) page = kids.getKey("/Pg");
        WalkKids(kids.getKey("/K"), kids, page, depth + 1);
    }

    QPDF& _pdf;
    std::map<size_t, std::string> const* _markerContents;
    std::map<QPDFObjGen, size_t> _pages;
    std::map<QPDFObjGen, std::string> _annotations;
    std::map<std::string, QPDFObjectHandle> _legacyDestinations;
    std::map<std::string, QPDFObjectHandle> _stringDestinations;
    std::set<QPDFObjGen> _destinationTrees;
    size_t _destinationNodes = 0;
    std::map<QPDFObjGen, size_t> _visited;
    std::set<QPDFObjGen> _layerIds;
    std::map<int, QPDFObjectHandle> _parentTree;
    std::map<QPDFObjGen, std::set<int>> _pageMcids;
    std::set<std::pair<QPDFObjGen, int>> _referencedMcids;
    std::set<QPDFObjGen> _elements;
    size_t _kidCount = 0;
    size_t _orphanMcids = 0;
    size_t _missingParentLinks = 0;
    size_t _mismatchedParentLinks = 0;
    size_t _detachedArtifacts = 0;
    std::string _result;
};

StructureBaseline InspectPdfStructure(QPDF& pdf)
{
    return StructureSnapshot(pdf).Capture(false);
}

void RequirePreservedPdfStructure(QPDF& output, StructureBaseline const& baseline,
    std::map<size_t, std::string> const* markerContents = nullptr)
{
    auto result = StructureSnapshot(output, markerContents).Capture(false);
    Require(result.orphanMcids == baseline.orphanMcids &&
        result.missingParentLinks == baseline.missingParentLinks &&
        result.mismatchedParentLinks == baseline.mismatchedParentLinks &&
        result.detachedArtifacts == baseline.detachedArtifacts &&
        result.semantics == baseline.semantics,
        "Output PDF changed tags, links, attachments, bookmarks or layers.");
}

void RequirePreservedPdfStructure(QPDF& original, QPDF& output)
{
    RequirePreservedPdfStructure(output, InspectPdfStructure(original));
}
