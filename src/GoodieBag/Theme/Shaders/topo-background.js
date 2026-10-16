// Animated topographic background (WebGL2). Look is fixed; only the line color is configurable.
// Perf: one fragment shader on a full-screen triangle, 30fps cap,
// stops when the window is hidden, draws once and idles under prefers-reduced-motion.
// Tuned look (approved Oct 2026): intensity 50% (17 lines), brightness 15%, size 60%, speed 100%, 100% res.

const FRAG = `#version 300 es
precision highp float;
uniform vec2 uRes;
uniform float uUnit, uZ;
uniform vec3 uColor, uBg;
out vec4 o;
const float DENSITY = 17.;   // contour lines per unit of height
const float BRIGHTNESS = .15;
// 3D simplex noise: Ashima Arts / Stefan Gustavson (MIT)
vec3 m289(vec3 x){return x-floor(x*(1./289.))*289.;}
vec4 m289(vec4 x){return x-floor(x*(1./289.))*289.;}
vec4 perm(vec4 x){return m289(((x*34.)+1.)*x);}
float snoise(vec3 v){
 const vec2 C=vec2(1./6.,1./3.);const vec4 D=vec4(0.,.5,1.,2.);
 vec3 i=floor(v+dot(v,C.yyy));vec3 x0=v-i+dot(i,C.xxx);
 vec3 g=step(x0.yzx,x0.xyz);vec3 l=1.-g;vec3 i1=min(g,l.zxy);vec3 i2=max(g,l.zxy);
 vec3 x1=x0-i1+C.xxx;vec3 x2=x0-i2+C.yyy;vec3 x3=x0-D.yyy;
 i=m289(i);
 vec4 p=perm(perm(perm(i.z+vec4(0.,i1.z,i2.z,1.))+i.y+vec4(0.,i1.y,i2.y,1.))+i.x+vec4(0.,i1.x,i2.x,1.));
 vec3 ns=.142857142857*D.wyz-D.xzx;
 vec4 j=p-49.*floor(p*ns.z*ns.z);vec4 x_=floor(j*ns.z);vec4 y_=floor(j-7.*x_);
 vec4 x=x_*ns.x+ns.yyyy;vec4 y=y_*ns.x+ns.yyyy;vec4 h=1.-abs(x)-abs(y);
 vec4 b0=vec4(x.xy,y.xy);vec4 b1=vec4(x.zw,y.zw);
 vec4 s0=floor(b0)*2.+1.;vec4 s1=floor(b1)*2.+1.;vec4 sh=-step(h,vec4(0.));
 vec4 a0=b0.xzyw+s0.xzyw*sh.xxyy;vec4 a1=b1.xzyw+s1.xzyw*sh.zzww;
 vec3 p0=vec3(a0.xy,h.x);vec3 p1=vec3(a0.zw,h.y);vec3 p2=vec3(a1.xy,h.z);vec3 p3=vec3(a1.zw,h.w);
 vec4 nr=1.79284291400159-.85373472095314*vec4(dot(p0,p0),dot(p1,p1),dot(p2,p2),dot(p3,p3));
 p0*=nr.x;p1*=nr.y;p2*=nr.z;p3*=nr.w;
 vec4 m=max(.6-vec4(dot(x0,x0),dot(x1,x1),dot(x2,x2),dot(x3,x3)),0.);m*=m;
 return 42.*dot(m*m,vec4(dot(p0,x0),dot(p1,x1),dot(p2,x2),dot(p3,x3)));
}
void main(){
 // uUnit = backing px per noise unit: terrain scale is fixed in CSS px, never stretched by window size.
 vec2 p=gl_FragCoord.xy/uUnit;
 float h=snoise(vec3(p,uZ))*.68+snoise(vec3(p*2.2+17.3,uZ*1.6))*.32;
 float v=h*DENSITY;
 float fw=fwidth(v);
 float d=abs(fract(v-.5)-.5)/fw;            // px distance to nearest contour
 float minor=1.-smoothstep(.4,1.3,d);
 float major=1.-smoothstep(.9,2.,d);
 float isMajor=1.-step(.5,mod(floor(v+.5),5.)); // every 5th line is an index contour
 float a=mix(minor*.45,major,isMajor);
 a*=1.-smoothstep(.35,.8,fw);               // fade where steep slopes would merge lines into mush; below 100% res this shows as dark blotches
 vec2 uv=gl_FragCoord.xy/uRes-.5;
 float vig=1.-smoothstep(.25,.85,length(uv));
 o=vec4(uBg+uColor*((h*.5+.5)*.05+a*BRIGHTNESS)*(.35+.65*vig),1.);
}`;

const RENDER_SCALE = 1; // native res: lower causes dark blotches at this line density
const UNIT_CSS_PX = 312; // noise unit in CSS px (520 * size 0.6)
const SPEED = 0.00004;   // z per ms
const FPS = 30;

const rgb = (hex) => { const n = parseInt(hex.replace('#', ''), 16); return [(n >> 16 & 255) / 255, (n >> 8 & 255) / 255, (n & 255) / 255]; };

export class TopoBackground {
  constructor(canvas, { color, bg }) {
    this.canvas = canvas;
    this.color = color; this.bg = bg;
    this.z = Math.random() * 100;
    this.raf = 0; this.last = 0; this.dirty = true;
    this.reduced = matchMedia('(prefers-reduced-motion: reduce)');
    this._tick = this._tick.bind(this);
    this._vis = () => (document.hidden ? this._stop() : this._start());
    this._lost = (e) => { e.preventDefault(); this._stop(); this.gl = null; };
    this._restored = () => { this._init(); this._start(); };
    this.ro = new ResizeObserver(() => { this.dirty = true; this._start(); });

    this._init();
    this.ro.observe(canvas);
    document.addEventListener('visibilitychange', this._vis);
    this.reduced.addEventListener('change', this._vis);
    canvas.addEventListener('webglcontextlost', this._lost);
    canvas.addEventListener('webglcontextrestored', this._restored);
    this._start();
  }

  /** Change line color and/or background (e.g. on theme switch). */
  setColors({ color = this.color, bg = this.bg }) {
    this.color = color; this.bg = bg;
    if (this.gl) this._applyColors();
    this._start();
  }

  destroy() {
    this._stop();
    this.ro.disconnect();
    document.removeEventListener('visibilitychange', this._vis);
    this.reduced.removeEventListener('change', this._vis);
    this.canvas.removeEventListener('webglcontextlost', this._lost);
    this.canvas.removeEventListener('webglcontextrestored', this._restored);
    this.gl?.getExtension('WEBGL_lose_context')?.loseContext();
    this.gl = null;
  }

  _init() {
    const gl = this.canvas.getContext('webgl2', { alpha: false, antialias: false, depth: false, stencil: false, powerPreference: 'low-power' });
    if (!gl) return false; // caller keeps the theme's normal background
    const sh = (type, src) => { const s = gl.createShader(type); gl.shaderSource(s, src); gl.compileShader(s); return s; };
    const prog = gl.createProgram();
    // Full-screen triangle from gl_VertexID: no vertex buffer.
    gl.attachShader(prog, sh(gl.VERTEX_SHADER, '#version 300 es\nvoid main(){vec2 p=vec2((gl_VertexID<<1)&2,gl_VertexID&2);gl_Position=vec4(p*2.-1.,0.,1.);}'));
    gl.attachShader(prog, sh(gl.FRAGMENT_SHADER, FRAG));
    gl.linkProgram(prog);
    if (!gl.getProgramParameter(prog, gl.LINK_STATUS)) { console.error(gl.getProgramInfoLog(prog)); return false; }
    gl.useProgram(prog);
    this.gl = gl;
    this.u = {};
    for (const n of ['uRes', 'uUnit', 'uZ', 'uColor', 'uBg']) this.u[n] = gl.getUniformLocation(prog, n);
    this._applyColors();
    this.dirty = true;
    return true;
  }

  get supported() { return !!this.gl; }

  _applyColors() {
    this.gl.uniform3fv(this.u.uColor, rgb(this.color));
    this.gl.uniform3fv(this.u.uBg, rgb(this.bg));
  }

  _start() { if (!this.raf && !document.hidden && this.gl) { this.last = performance.now(); this.raf = requestAnimationFrame(this._tick); } }
  _stop() { cancelAnimationFrame(this.raf); this.raf = 0; }

  _tick(now) {
    this.raf = 0;
    const animated = !this.reduced.matches;
    const dt = now - this.last;
    if (animated && dt < 1000 / FPS - 1) { this.raf = requestAnimationFrame(this._tick); return; }
    this.last = now;
    // Wrap z so float precision never degrades over long sessions.
    if (animated) this.z = (this.z + Math.min(dt, 100) * SPEED) % 256;
    this._draw();
    if (animated) this.raf = requestAnimationFrame(this._tick);
  }

  _draw() {
    const gl = this.gl; if (!gl) return;
    const k = (devicePixelRatio || 1) * RENDER_SCALE;
    if (this.dirty) {
      const w = Math.max(1, Math.round(this.canvas.clientWidth * k));
      const h = Math.max(1, Math.round(this.canvas.clientHeight * k));
      if (this.canvas.width !== w || this.canvas.height !== h) { this.canvas.width = w; this.canvas.height = h; }
      gl.viewport(0, 0, w, h);
      gl.uniform2f(this.u.uRes, w, h);
      gl.uniform1f(this.u.uUnit, UNIT_CSS_PX * k);
      this.dirty = false;
    }
    gl.uniform1f(this.u.uZ, this.z);
    gl.drawArrays(gl.TRIANGLES, 0, 3);
  }
}
