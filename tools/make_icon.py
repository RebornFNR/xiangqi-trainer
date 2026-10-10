# 生成程序图标：红方“帅”棋子
import os
from PIL import Image, ImageDraw, ImageFont

OUT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "assets", "icon.ico")
S = 256


def make_base():
    img = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    pad = 6
    # 木色底
    d.ellipse([pad, pad, S - pad, S - pad], fill=(243, 224, 190, 255),
              outline=(150, 104, 52, 255), width=8)
    d.ellipse([pad + 16, pad + 16, S - pad - 16, S - pad - 16],
              outline=(176, 32, 32, 255), width=5)
    font = ImageFont.truetype(r"C:\Windows\Fonts\simkai.ttf", 150)
    text = "帅"
    box = d.textbbox((0, 0), text, font=font)
    w, h = box[2] - box[0], box[3] - box[1]
    d.text(((S - w) / 2 - box[0], (S - h) / 2 - box[1] - 4), text, font=font,
           fill=(176, 32, 32, 255))
    return img


base = make_base()
os.makedirs(os.path.dirname(OUT), exist_ok=True)
base.save(OUT, sizes=[(16, 16), (24, 24), (32, 32), (48, 48), (64, 64), (128, 128), (256, 256)])
print("icon written:", os.path.normpath(OUT))
