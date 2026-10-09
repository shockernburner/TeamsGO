# Draws the HUSHCLAW logo, its tagline, the app icon and the Vantward Games logos as SVG (all text uses Oswald,
# OFL, in fonts/). To regenerate the PNGs in Assets/_Project/Art/Brand/Resources/Brand:
#   python3 make.py && cp fonts/*.woff2 svg/ && PW=$(npm root -g)/playwright node render.js   (writes png/)
import os
D = os.path.dirname(os.path.abspath(__file__))
BONE, AMBER, RUST, INK = '#ECE4D2', '#E3A23B', '#B4532A', '#14110D'
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

def hushclaw(cx, cy, size, idp='h'):
    # HUSHCLAW in bone, cut by three claw slashes across the C, with a thin amber rule under it.
    tw = size*4.9
    out = [f'<defs><mask id="{idp}m" maskUnits="userSpaceOnUse" x="-5000" y="-5000" width="10000" height="10000">'
           f'<rect x="-5000" y="-5000" width="10000" height="10000" fill="white"/>'] + claws(cx+size*0.31, cy-size*0.40, size/170) + ['</mask></defs>']
    out.append(f'<text x="{cx}" y="{cy}" text-anchor="middle" font-family="Osw" font-weight="700" font-size="{size}" '
               f'letter-spacing="{size*0.06}" textLength="{tw}" lengthAdjust="spacing" fill="{BONE}" mask="url(#{idp}m)">HUSHCLAW</text>')
    out.append(f'<rect x="{cx-tw/2}" y="{cy+size*0.12}" width="{tw}" height="{max(4,size*0.045)}" rx="{max(2,size*0.02)}" fill="{AMBER}"/>')
    return out

def tagline(cx, cy, size, text='STAY QUIET. STAY CLOSE.', color=AMBER):
    w = size*len(text)*0.62
    out = [f'<text x="{cx}" y="{cy}" text-anchor="middle" font-family="Osw" font-weight="500" font-size="{size}" '
           f'letter-spacing="{size*0.18}" textLength="{w}" lengthAdjust="spacing" fill="{color}">{text}</text>']
    for sx in (-1, 1):
        x_in, x_out = cx + sx*(w/2 + size*0.5), cx + sx*(w/2 + size*2.2)
        out.append(f'<rect x="{min(x_in,x_out)}" y="{cy-size*0.38}" width="{abs(x_out-x_in)}" height="{max(3,size*0.06)}" fill="{color}"/>')
    return out

files = {}
files['Hushclaw'] = svg(1300, 330, hushclaw(650, 250, 250))
files['Tagline'] = svg(1000, 160, tagline(500, 110, 70))
files['HushclawLogo'] = svg(1300, 470, hushclaw(650, 250, 250, idp='g') + tagline(650, 420, 62))

vm = vmark(60, 40)
vw = ['<g>'] + vm + ['</g>',
      f'<text x="390" y="214" font-family="Osw" font-weight="600" font-size="150" textLength="780" lengthAdjust="spacing" fill="{BONE}">VANTWARD</text>',
      f'<text x="780" y="296" text-anchor="middle" font-family="Osw" font-weight="500" font-size="56" textLength="250" lengthAdjust="spacing" fill="{AMBER}">GAMES</text>',
      f'<rect x="392" y="266" width="236" height="5" fill="{AMBER}"/>',
      f'<rect x="932" y="266" width="236" height="5" fill="{AMBER}"/>']
files['VantwardGames'] = svg(1230, 380, vw)
files['VantwardMark'] = svg(380, 380, vmark(60, 40))

# app icon: dark tile, three amber claw slashes with a bone H behind them
icon = [f'<defs><radialGradient id="gl" cx="50%" cy="42%" r="65%"><stop offset="0" stop-color="#3A2A18"/><stop offset="1" stop-color="{INK}"/></radialGradient>'
        f'<mask id="im" maskUnits="userSpaceOnUse" x="0" y="0" width="1024" height="1024"><rect width="1024" height="1024" fill="white"/>'] + \
       claws(512, 512, 3.0) + ['</mask></defs>',
        '<rect width="1024" height="1024" fill="url(#gl)"/>',
        f'<text x="512" y="760" text-anchor="middle" font-family="Osw" font-weight="700" font-size="700" fill="{BONE}" mask="url(#im)">H</text>',
        '<g transform="translate(26,-18)">' + ''.join(c.replace('fill="black"', f'fill="{AMBER}"') for c in claws(512, 512, 2.6)) + '</g>']
files['Icon1024'] = svg(1024, 1024, icon)

os.makedirs(os.path.join(D, 'svg'), exist_ok=True)
for k, v in files.items():
    open(os.path.join(D, 'svg', k + '.svg'), 'w').write(v)
print(' '.join(files))
