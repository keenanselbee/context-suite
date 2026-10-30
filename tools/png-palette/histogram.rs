// Independently authored compatibility adapter for ExoQuant 0.2.0's histogram API.
// Keep randomized hash insertion; canonicalize traversal before any float work.
use std::collections::HashMap;
use std::iter::FromIterator;
use super::{Color,Colorf,ColorSpace};

pub struct Histogram { data: HashMap<Color,usize> }
#[derive(Clone)]
pub struct ColorCount { pub color: Colorf, pub count: usize }
impl Histogram {
    pub fn new() -> Histogram { Histogram { data: HashMap::new() } }
    pub fn iter<'a>(&'a self) -> Box<dyn Iterator<Item=(&'a Color,&'a usize)>+'a> {
        let mut entries:Vec<_>=self.data.iter().collect();
        entries.sort_unstable_by_key(|&(c,_)| [c.r,c.g,c.b,c.a]);
        Box::new(entries.into_iter())
    }
    pub fn to_color_counts(&self,space:&dyn ColorSpace) -> Vec<ColorCount> {
        self.iter().map(|(c,n)|ColorCount {color:space.to_float(*c),count:*n}).collect()
    }
}
impl Extend<Color> for Histogram {
    fn extend<T:IntoIterator<Item=Color>>(&mut self,values:T) {
        for c in values { *self.data.entry(c).or_insert(0)+=1; }
    }
}
impl FromIterator<Color> for Histogram {
    fn from_iter<T:IntoIterator<Item=Color>>(values:T) -> Self {
        let mut histogram=Histogram::new();histogram.extend(values);histogram
    }
}
#[cfg(test)]
mod stable_tests {
    use super::*;
    use super::super::{generate_palette,SimpleColorSpace,optimizer};
    use super::super::optimizer::Optimizer;
    fn entries(h:&Histogram)->Vec<([u8;4],usize)> {
        h.iter().map(|(c,n)|([c.r,c.g,c.b,c.a],*n)).collect()
    }
    #[test]
    fn counts_hidden_rgb_alpha_and_empty() {
        let mut h=Histogram::new();assert!(entries(&h).is_empty());
        h.extend(vec![Color::new(2,3,4,0),Color::new(1,3,4,0),Color::new(2,3,4,0),Color::new(2,3,4,255)]);
        assert_eq!(entries(&h),vec![([1,3,4,0],1),([2,3,4,0],2),([2,3,4,255],1)]);
    }
    #[test]
    fn permutations_and_fresh_hash_seeds_have_identical_palettes() {
        let pixels:Vec<_>=(0..128u16).map(|i|Color::new((i%8*32) as u8,((i/8)%8*32) as u8,((i/64)*128) as u8,255)).collect();
        let space=SimpleColorSpace::default();let opt=optimizer::KMeans;
        let mut expected=None;
        for rotation in 0..12 {
            let mut order=pixels.clone();order.rotate_left(rotation*7);if rotation%2==1 {order.reverse();}
            let h:Histogram=order.into_iter().collect();
            let p=generate_palette(&h,&space,&opt,16);
            let p=opt.optimize_palette(&space,&p,&h,16);
            let value=(entries(&h),p.iter().map(|c|[c.r,c.g,c.b,c.a]).collect::<Vec<_>>());
            if let Some(ref previous)=expected {assert_eq!(&value,previous);} else {expected=Some(value);}
        }
    }
}
