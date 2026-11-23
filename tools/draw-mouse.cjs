// 三格共用同一外壳和滚轮，优先由爪子遮住按钮的上缘。
const fs = require('fs');
const path = require('path');
const { createCanvas, Path2D } = require('@napi-rs/canvas');
const width = 40, height = 36, scale = 6;
const canvas = createCanvas(3 * width * scale, height * scale);
const ctx = canvas.getContext('2d');
ctx.scale(scale, scale);
ctx.lineJoin = 'round';
ctx.lineCap = 'round';
const ink = '#513a27';

function shape(commands, fill, stroke, weight) {
  const outline = new Path2D(commands);
  if (fill) { ctx.fillStyle = fill; ctx.fill(outline); }
  if (stroke) { ctx.strokeStyle = stroke; ctx.lineWidth = weight; ctx.stroke(outline); }
}

const top = 'M 4.8 16.4 C 7.5 10.2 15.2 3.6 21.5 2.3 C 27.8 .8 34.4 3.5 36.9 9.1 C 39.1 14.2 36 21 30.4 25.7 C 26.7 29.2 22.1 31.1 16.7 31.1 C 7.6 31.1 1.1 25.8 4.8 16.4 Z';
for (let frame = 0; frame < 3; frame++) {
  ctx.save();
  ctx.translate(frame * width, 0);
  shape('M 4.1 18 C 8.1 10.6 15.6 5.7 23.5 4.7 C 30 4.3 36.8 7.2 37.4 12.2 C 39.4 18.7 35.5 24.4 29.4 29.7 C 25.7 33.1 20.7 34.5 15.3 33.8 C 6.3 32.8 1.2 27.2 3.9 19.2 Z', '#ead9b7', ink, 1.45);
  shape(top, '#c1c8a5', ink, 1.18);
  shape('M 27.7 4.6 C 22.9 3.5 17.6 5.7 14.8 7.7', null, '#e0e3c8', .9);
  // 从豚鼠一侧看，左键位于画面右下，右键位于左上。
  if (frame !== 0) {
    ctx.save();
    ctx.clip(new Path2D(top));
    const button = frame === 1
      ? 'M 22.4 17.6 Q 29.9 21.9 32.6 21.5 Q 27.3 32.5 15 32.2 Q 10.5 31.9 8.9 28.7 Q 15.8 23.9 22.4 17.6 Z'
      : 'M 7.2 12.7 Q 12.1 14.5 16.9 17.4 Q 14.1 21.9 6.3 27.3 Q 1.1 23.1 4.3 16.1 Z';
    shape(button, '#acb694', null, 0);
    ctx.restore();
  }
  shape('M 8.7 28.7 Q 12.4 25.9 15.7 22.7 M 20.3 17.5 L 21.9 15.9', null, '#775e40', .79);
  shape('M 8.5 12.8 Q 12.4 13.7 15.2 15.6 M 22.5 18.1 Q 28.1 21.5 32.8 21.6', null, '#899270', .55);
  ctx.save();
  ctx.translate(18.3, 20.0);
  ctx.rotate(.72);
  shape('M -1.7 -2.8 Q -1.7 -4.3 .1 -4.3 Q 1.9 -4.3 1.9 -2.6 L 1.7 2.7 Q 1.5 4.1 -.1 4.1 Q -1.9 4 -1.9 2.5 Z', '#879579', ink, .83);
  shape('M -.7 -1.9 L .8 -1.9 M -.8 -.2 L .8 -.2 M -.9 1.4 L .7 1.4', null, '#c8cfb2', .55);
  ctx.restore();
  ctx.restore();
}
fs.writeFileSync(path.join(__dirname, '..', 'assets', 'mouse.png'), canvas.toBuffer('image/png'));
