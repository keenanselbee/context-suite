use exoquant::{Color, Colorf, ColorSpace, SimpleColorSpace};

// Corrected adapter: invert the upstream forward transform in reverse operation order.
// Keep all quantization/dithering upstream code untouched.
pub struct VerifiedSpace(SimpleColorSpace);
impl VerifiedSpace {
    pub fn new() -> Self {
        let space=Self(SimpleColorSpace::default());
        for alpha in [0,1,128,255] {
            for value in 0..=255 {
                let c=Color::new(value,255-value,value/2,alpha);
                assert!(space.from_float(space.to_float(c))==c,"Colour roundtrip failed");
            }
        }

        space
    }
}
impl ColorSpace for VerifiedSpace {
    fn to_linear(&self,c:Colorf)->Colorf { self.0.to_linear(c) }
    fn from_linear(&self,c:Colorf)->Colorf {
        let factor=c.a*(1.0-self.0.transparency_scale)+self.0.transparency_scale;
        (Colorf{r:c.r/factor,g:c.g/factor,b:c.b/factor,a:c.a}/self.0.scale).pow(1.0/self.0.gamma)
    }
    fn to_dither(&self,c:Colorf)->Colorf { self.0.to_dither(c) }
    fn from_dither(&self,c:Colorf)->Colorf { self.0.from_dither(c) }
    fn from_float(&self,c:Colorf)->Color {
        let c=self.from_linear(c);
        let channel=|v:f64| (v*255.0).round().clamp(0.0,255.0) as u8;
        Color::new(channel(c.r),channel(c.g),channel(c.b),channel(c.a))
    }
}
