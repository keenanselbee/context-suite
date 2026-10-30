use exoquant::{Color,Colorf,ColorMap,ColorSpace,Histogram,generate_palette,optimizer};
use exoquant::optimizer::Optimizer;
use std::{collections::{BTreeMap,HashSet},io::{self,Read,Write}};
#[path="space.rs"] mod corrected;


fn palette(hist:&Histogram,count:usize,anchors:usize,space:&corrected::VerifiedSpace)->Vec<Color> {
    let opt=optimizer::KMeans;let count=count.min(hist.iter().count()).max(1);let anchors=anchors.min(count.saturating_sub(1));
    let p=generate_palette(hist,space,&opt,count-anchors);let mut p=opt.optimize_palette(space,&p,hist,16);
    let counts:Vec<_>=hist.iter().map(|(&c,&n)|(c,n)).collect();
    for _ in 0..anchors {
        let map=ColorMap::new(&p,space);let mut best=None;let mut score=-1.0;
        for &(c,n) in &counts {
            let near=p[map.find_nearest(space.to_float(c))];
            let error=c.r.abs_diff(near.r).max(c.g.abs_diff(near.g)).max(c.b.abs_diff(near.b)) as f64;
            let value=error*error*(n as f64).sqrt().min(8.0);
            if value>score {score=value;best=Some(c);}
        }
        p.push(best.unwrap());
    }
    if anchors>0 {
        let pinned:Vec<_>=p[p.len()-anchors..].iter().map(|&c|space.to_float(c)).collect();
        let hist=hist.to_color_counts(space);let mut colors:Vec<_>=p.iter().map(|&c|space.to_float(c)).collect();
        for _ in 0..8 {colors=opt.step(colors,&hist);let end=colors.len();colors[end-anchors..].copy_from_slice(&pinned);}
        p=colors.iter().map(|&c|space.from_float(c)).collect();
    }
    p
}
fn edge(pixels:&[Color],width:usize,i:usize)->f64 {
    let x=i%width;let y=i/width;let h=pixels.len()/width;let c=pixels[i];let mut contrast=0;
    for yy in y.saturating_sub(1)..=(y+1).min(h-1) {for xx in x.saturating_sub(1)..=(x+1).min(width-1) {
        let n=pixels[yy*width+xx];contrast=contrast.max(c.r.abs_diff(n.r)).max(c.g.abs_diff(n.g)).max(c.b.abs_diff(n.b)).max(c.a.abs_diff(n.a));
    }}
    if contrast<=4 {1.0}else if contrast>=24 {0.0}else {(24-contrast) as f64/20.0}
}
fn remap(pixels:&[Color],width:usize,palettes:&BTreeMap<u8,Vec<Color>>,space:&corrected::VerifiedSpace)->Vec<Color> {
    let maps:BTreeMap<_,_>=palettes.iter().map(|(&a,p)|(a,ColorMap::new(p,space))).collect();
    let height=pixels.len()/width;let mut current=vec![Colorf::zero();width];let mut next=current.clone();let mut out=pixels.to_vec();
    for y in 0..height {
        let forward=y%2==0;let dir=if forward {1isize}else {-1};
        for step in 0..width {
            let x=if forward {step}else {width-1-step};let i=y*width+x;let c=pixels[i];
            if c.a==0 {continue;}
            let map=&maps[&c.a];let p=&palettes[&c.a];let gate=edge(pixels,width,i);
            let input=space.to_dither(space.to_float(c));let target=input+current[x]*gate;
            let index=map.find_nearest(space.from_dither(target));out[i]=p[index];
            assert_eq!(out[i].a,c.a);
            let error=(target-space.to_dither(map.float_color(index)))*(0.5*gate);
            for (dx,dy,factor) in [(1isize,0usize,7.0/16.0),(-1,1,3.0/16.0),(0,1,5.0/16.0),(1,1,1.0/16.0)] {
                let xx=x as isize+dx*dir;let yy=y+dy;
                if xx>=0 && xx<width as isize && yy<height && pixels[yy*width+xx as usize].a==c.a {
                    if dy==0 {current[xx as usize]+=error*factor;}else {next[xx as usize]+=error*factor;}
                }
            }
        }
        std::mem::swap(&mut current,&mut next);next.fill(Colorf::zero());
    }
    out
}

fn run()->Result<(),String> {
    let mut input=io::stdin().lock();let mut header=[0u8;8];
    input.read_exact(&mut header).map_err(|_|"Invalid header")?;
    let width=u32::from_le_bytes(header[..4].try_into().unwrap()) as usize;
    let height=u32::from_le_bytes(header[4..].try_into().unwrap()) as usize;
    let count=width.checked_mul(height).filter(|&n|n>0 && n<=9_000_000).ok_or("Input bounds")?;
    let mut raw=vec![0u8;count*4];input.read_exact(&mut raw).map_err(|_|"Incomplete pixels")?;
    if input.read(&mut [0u8;1]).map_err(|_|"Input failure")?!=0 {return Err("Excess input".into());}
    let pixels:Vec<_>=raw.chunks_exact(4).map(|v|Color::new(v[0],v[1],v[2],v[3])).collect();
    let space=corrected::VerifiedSpace::new();
    let mut groups:BTreeMap<u8,Histogram>=BTreeMap::new();
    for &c in &pixels {groups.entry(c.a).or_insert_with(Histogram::new).extend([c]);}
    let hidden=groups.get(&0).map_or(0,|h|h.iter().count());
    let partial:Vec<_>=groups.keys().copied().filter(|&a|a>0 && a<255).collect();
    if !groups.contains_key(&255) || hidden+partial.len()>=128 {return Err("Unsupported alpha allocation".into());}
    let full:Histogram=pixels.iter().copied().collect();
    let seed=palette(&full,256,0,&space);let seed_map=ColorMap::new(&seed,&space);
    let nearest:Vec<_>=pixels.iter().map(|&c|seed[seed_map.find_nearest(space.to_float(c))]).collect();
    let reserve=if partial.is_empty(){0}else{16usize.max(partial.len())};
    let mut counts:BTreeMap<u8,usize>=partial.iter().map(|&a|(a,1)).collect();
    let variance:BTreeMap<_,_>=partial.iter().map(|&a|{
        let entries=groups[&a].to_color_counts(&space);let n:usize=entries.iter().map(|c|c.count).sum();
        let mean=entries.iter().fold(Colorf::zero(),|s,c|s+c.color*c.count as f64)*(1.0/n as f64);
        let v:f64=entries.iter().map(|c|{let d=c.color-mean;d.dot(&d)*c.count as f64}).sum();(a,v)
    }).collect();
    for _ in partial.len()..reserve {
        let selected=partial.iter().copied().filter(|a|counts[a]<groups[a].iter().count()).max_by(|a,b|{
            let score=|a:&u8|variance[a]/(counts[a]*(counts[a]+1)) as f64;
            score(a).total_cmp(&score(b)).then_with(||b.cmp(a))
        });
        if let Some(a)=selected {*counts.get_mut(&a).unwrap()+=1;}else{break;}
    }
    let mut opaque_hist=Histogram::new();
    for (i,&c) in pixels.iter().enumerate().filter(|(_,c)|c.a==255) {
        let n=nearest[i];let error=c.r.abs_diff(n.r).max(c.g.abs_diff(n.g)).max(c.b.abs_diff(n.b));
        let mut weight=if error>=12 {4}else if error>=6 {2}else{1};
        if c.r.max(c.g).max(c.b)<=32 && edge(&pixels,width,i)>=0.8 {weight=weight.max(4);}
        opaque_hist.extend(std::iter::repeat_n(c,weight));
    }
    let mut opaque_palette=palette(&opaque_hist,256-hidden-reserve,8,&space);
    for c in &mut opaque_palette {c.a=255;}
    let mut palettes=BTreeMap::new();palettes.insert(255,opaque_palette);
    for &a in &partial {
        let mut p=palette(&groups[&a],counts[&a],0,&space);
        for c in &mut p {c.a=a;}
        palettes.insert(a,p);
    }
    let mapped=remap(&pixels,width,&palettes,&space);
    let unique:HashSet<_>=mapped.iter().map(|c|[c.r,c.g,c.b,c.a]).collect();
    if unique.len()>256 {return Err("Palette budget".into());}
    for (s,m) in pixels.iter().zip(&mapped) {
        if s.a!=m.a || (s.a==0 && s!=m) {return Err("Preservation failure".into());}
    }
    let output:Vec<_>=mapped.iter().flat_map(|c|[c.r,c.g,c.b,c.a]).collect();
    io::stdout().lock().write_all(&output).map_err(|_|"Output failure")?;
    Ok(())
}
fn main() {
    if let Err(message)=run(){eprintln!("{}",message);std::process::exit(1);}
}
