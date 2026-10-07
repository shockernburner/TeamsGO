# Draws the TETHER, Primal and Vantward Games logos as SVG (TETHER's letters are plain shapes; Primal and
# Vantward use Oswald, OFL, in fonts/). To regenerate the PNGs in Assets/_Project/Art/Brand/Resources/Brand:
#   python3 make.py && cp fonts/*.woff2 svg/ && PW=$(npm root -g)/playwright node render.js   (writes png/)
import os
D = os.path.dirname(os.path.abspath(__file__))
BONE, AMBER, RUST, INK = '#ECE4D2', '#E3A23B', '#B4532A', '#14110D'
s, g, H = 46, 10, 220

def T(x):  return [f'<rect x="{x}" y="0" width="160" height="{s}"/>', f'<rect x="{x+57}" y="{s+g}" width="{s}" height="{H-s-g}"/>']
def E(x):  return [f'<rect x="{x}" y="0" width="{s}" height="{H}"/>', f'<rect x="{x+s+g}" y="0" width="{130-s-g}" height="{s}"/>',
                   f'<rect x="{x+s+g}" y="{(H-s)/2}" width="{112-s-g}" height="{s}"/>', f'<rect x="{x+s+g}" y="{H-s}" width="{130-s-g}" height="{s}"/>']
def Hh(x): return [f'<rect x="{x}" y="0" width="{s}" height="{H}"/>', f'<rect x="{x+150-s}" y="0" width="{s}" height="{H}"/>',
                   f'<rect x="{x+s+g}" y="{(H-s)/2}" width="{150-2*s-2*g}" height="{s}"/>']
def R(x):
    a = x+s+g; b = 124
    bowl = (f'<path fill-rule="evenodd" d="M{a} 0 H{x+88} A62 62 0 0 1 {x+88} {b} H{a} Z '
            f'M{a} {s} H{x+88} A16 16 0 0 1 {x+88} {b-s} H{a} Z"/>')
    leg = f'<polygon points="{x+62},{b+g} {x+110},{b+g} {x+152},{H} {x+104},{H}"/>'
    return [f'<rect x="{x}" y="0" width="{s}" height="{H}"/>', bowl, leg]

def tether_word(ox, oy, line=True):
    parts, x, hooks = [], ox, []
    for f, w, hk in [(T,160,[80]),(E,130,[23]),(T,160,[80]),(Hh,150,[23,127]),(E,130,[23]),(R,152,[23])]:
        parts += f(x); hooks += [x+h for h in hk]; x += w + 30
    out = [f'<g transform="translate(0,{oy})" fill="{BONE}">'] + parts + ['</g>']
    if line:
        y = oy - 44
        out.append(f'<g fill="{AMBER}"><rect x="{ox-70}" y="{y}" width="{x-30-ox+140}" height="9" rx="4.5"/>')
        for h in hooks: out.append(f'<rect x="{h-4}" y="{y+4}" width="8" height="{44-4}"/>')
        out.append('</g>')
        for cx in (ox-88, x-30+88):
            out.append(f'<circle cx="{cx}" cy="{y+4.5}" r="18" fill="none" stroke="{AMBER}" stroke-width="9"/>')
    return out, x - 30

FONT = '''<style>@font-face{font-family:Osw;font-weight:700;src:url(oswald-latin-700-normal.woff2)}
@font-face{font-family:Osw;font-weight:600;src:url(oswald-latin-600-normal.woff2)}
@font-face{font-family:Osw;font-weight:500;src:url(oswald-latin-500-normal.woff2)}
@font-face{font-family:Osw;font-weight:400;src:url(oswald-latin-400-normal.woff2)}</style>'''

def claws(cx, cy, scale):
    # three tapered, slightly curved slashes, top-right to bottom-left
    out = []
    for i, dx in enumerate((-34, 0, 34)):
        x0, y0 = cx + dx*scale + 70*scale, cy - 95*scale
        x1, y1 = cx + dx*scale - 70*scale, cy + 95*scale
        w = 15*scale
        out.append(f'<path d="M{x0} {y0} Q{(x0+x1)/2+18*scale} {(y0+y1)/2} {x1} {y1} '
                   f'Q{(x0+x1)/2+18*scale+w} {(y0+y1)/2+w*0.2} {x0+w*0.6} {y0+w*0.3} Z" fill="black"/>')
    return out

def primal(cx, cy, size, color=AMBER, rules=True, idp='p'):
    out = [f'<defs><mask id="{idp}m" maskUnits="userSpaceOnUse" x="-5000" y="-5000" width="10000" height="10000">'
           f'<rect x="-5000" y="-5000" width="10000" height="10000" fill="white"/>'] + claws(cx+size*0.15, cy-size*0.36, size/62) + ['</mask></defs>']
    out.append(f'<text x="{cx}" y="{cy}" text-anchor="middle" font-family="Osw" font-weight="700" font-size="{size}" '
               f'letter-spacing="{size*0.32}" textLength="{size*4.6}" lengthAdjust="spacing" fill="{color}" mask="url(#{idp}m)">PRIMAL</text>')
    if rules:
        half = size*2.55
        for sx in (-1, 1):
            x_in, x_out = cx + sx*(size*2.6), cx + sx*(size*3.7)
            out.append(f'<rect x="{min(x_in,x_out)}" y="{cy-size*0.38}" width="{abs(x_out-x_in)}" height="{max(3,size*0.05)}" fill="{color}"/>')
    return out

def vmark(ox, oy, k=1.0, c1=AMBER, c2=BONE):
    P = lambda pts: ' '.join(f'{ox+x*k},{oy+y*k}' for x, y in pts)
    return [f'<polygon fill="{c2}" points="{P([(0,0),(56,0),(118,176),(90,256)])}"/>',
            f'<polygon fill="{c1}" points="{P([(112,256),(208,0),(268,0),(150,290)])}"/>' if False else
            f'<polygon fill="{c1}" points="{P([(104,262),(196,0),(254,0),(134,300)])}"/>']

def svg(w, h, body, bg=None):
    b = f'<rect width="{w}" height="{h}" fill="{bg}"/>' if bg else ''
    return f'<svg xmlns="http://www.w3.org/2000/svg" width="{w}" height="{h}" viewBox="0 0 {w} {h}">{FONT}{b}{"".join(body)}</svg>'

files = {}
word, wx = tether_word(120, 100)
files['Tether'] = svg(wx+120, 360, word)

files['Primal'] = svg(1000, 220, primal(500, 150, 110))

word2, wx2 = tether_word(120, 100)
files['TetherPrimal'] = svg(wx2+120, 520, word2 + primal((wx2+120)/2, 450, 92, idp='q'))

vm = vmark(60, 40)
vw = ['<g>'] + vm + ['</g>',
      f'<text x="390" y="214" font-family="Osw" font-weight="600" font-size="150" textLength="780" lengthAdjust="spacing" fill="{BONE}">VANTWARD</text>',
      f'<text x="780" y="296" text-anchor="middle" font-family="Osw" font-weight="500" font-size="56" textLength="250" lengthAdjust="spacing" fill="{AMBER}">GAMES</text>',
      f'<rect x="392" y="266" width="236" height="5" fill="{AMBER}"/>',
      f'<rect x="932" y="266" width="236" height="5" fill="{AMBER}"/>']
files['VantwardGames'] = svg(1230, 380, vw)
files['VantwardMark'] = svg(380, 380, vmark(60, 40))

# app icon: dark tile, one hanging stencil T from the tether line, claw slashes behind
icon = [f'<defs><radialGradient id="gl" cx="50%" cy="42%" r="65%"><stop offset="0" stop-color="#3A2A18"/><stop offset="1" stop-color="{INK}"/></radialGradient></defs>',
        '<rect width="1024" height="1024" fill="url(#gl)"/>',
        '<g opacity="0.16" transform="translate(0,0)">' + ''.join(c.replace('fill="black"', f'fill="{AMBER}"') for c in claws(560, 640, 3.4)) + '</g>',
        f'<g transform="translate(232,330) scale(3.5)" fill="{BONE}">'] + T(0) + ['</g>',
        f'<rect x="80" y="232" width="864" height="30" rx="15" fill="{AMBER}"/>',
        f'<rect x="498" y="246" width="28" height="86" fill="{AMBER}"/>',
        f'<circle cx="80" cy="247" r="44" fill="none" stroke="{AMBER}" stroke-width="26"/>',
        f'<circle cx="944" cy="247" r="44" fill="none" stroke="{AMBER}" stroke-width="26"/>']
files['Icon1024'] = svg(1024, 1024, icon)

os.makedirs(os.path.join(D, 'svg'), exist_ok=True)
for k, v in files.items():
    open(os.path.join(D, 'svg', k + '.svg'), 'w').write(v)
print(' '.join(files))
