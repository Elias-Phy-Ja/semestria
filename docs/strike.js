// The lightning in the hero: one strike on load that lights up the exam of the week.
// The seed is fixed, so everyone sees the same bolt and a resize only rescales it.
(function () {
  'use strict';

  var hero   = document.getElementById('hero');
  var canvas = document.getElementById('bolt');
  var target = document.getElementById('strike-target');
  var ctx    = canvas && canvas.getContext && canvas.getContext('2d');
  if (!hero || !target || !ctx) return;

  var narrow = window.innerWidth < 760;
  var REST = narrow ? 0.34 : 0.5;   // brightness the bolt keeps once the strike is over
  var calm = window.matchMedia('(prefers-reduced-motion: reduce)').matches;

  function random(seed) {
    return function () {
      seed = (seed + 0x6D2B79F5) | 0;
      var t = Math.imul(seed ^ (seed >>> 15), 1 | seed);
      t = (t + Math.imul(t ^ (t >>> 7), 61 | t)) ^ t;
      return ((t ^ (t >>> 14)) >>> 0) / 4294967296;
    };
  }

  // Midpoint displacement: split every segment and push its middle sideways.
  function jagged(from, to, spread, rounds, rnd) {
    var points = [from, to];
    for (var round = 0; round < rounds; round++) {
      var next = [points[0]];
      for (var i = 1; i < points.length; i++) {
        var a = points[i - 1], b = points[i];
        var dx = b.x - a.x, dy = b.y - a.y;
        var push = (rnd() - 0.5) * spread;
        next.push({ x: (a.x + b.x) / 2 - dy * push, y: (a.y + b.y) / 2 + dx * push }, b);
      }
      points = next;
    }
    return points;
  }

  function build(width, height) {
    var box  = hero.getBoundingClientRect();
    var spot = target.getBoundingClientRect();
    var rnd  = random(2026);

    var hit   = { x: spot.left - box.left + spot.width / 2, y: spot.top - box.top + 2 };
    var start = { x: width * 0.985, y: -24 };
    var main  = jagged(start, hit, narrow ? 0.24 : 0.42, 7, rnd);
    var bolts = [{ points: main, width: 1, at: 0 }];

    for (var i = 0; i < 6; i++) {
      var at     = 0.14 + i * 0.13;
      var origin = main[Math.floor(at * (main.length - 1))];
      var side   = i % 2 ? 1 : -1;
      var reach  = height * (narrow ? 0.05 : 0.1) * (1 + rnd() * 1.6);
      var end    = { x: origin.x + side * reach * (0.5 + rnd() * 0.6), y: origin.y + reach };
      bolts.push({ points: jagged(origin, end, 0.5, 5, rnd), width: 0.45, at: at });
    }
    return bolts;
  }

  function stroke(points, count, width, color, glow) {
    ctx.beginPath();
    ctx.moveTo(points[0].x, points[0].y);
    for (var i = 1; i < count; i++) ctx.lineTo(points[i].x, points[i].y);
    ctx.lineWidth   = width;
    ctx.strokeStyle = color;
    ctx.shadowColor = '#2F7BFF';
    ctx.shadowBlur  = glow;
    ctx.stroke();
  }

  function draw(bolts, progress, brightness) {
    ctx.clearRect(0, 0, canvas.clientWidth, canvas.clientHeight);
    ctx.globalCompositeOperation = 'lighter';
    ctx.globalAlpha = brightness;
    ctx.lineJoin = 'round';

    bolts.forEach(function (bolt) {
      var share = (progress - bolt.at) / (1 - bolt.at);
      if (share <= 0) return;
      var count = Math.max(2, Math.ceil(bolt.points.length * Math.min(share, 1)));
      stroke(bolt.points, count, 7 * bolt.width, 'rgba(47,123,255,.55)', 30);
      stroke(bolt.points, count, 3 * bolt.width, '#5AB4FF', 12);
      stroke(bolt.points, count, 1.4 * bolt.width, '#EAFBFF', 0);
    });
  }

  function fit() {
    var scale  = Math.min(window.devicePixelRatio || 1, 2);
    var width  = canvas.clientWidth;
    var height = canvas.clientHeight;
    canvas.width  = width * scale;
    canvas.height = height * scale;
    ctx.setTransform(scale, 0, 0, scale, 0, 0);
    return build(width, height);
  }

  var bolts = fit();

  var resizing;
  window.addEventListener('resize', function () {
    clearTimeout(resizing);
    resizing = setTimeout(function () {
      bolts = fit();
      draw(bolts, 1, REST);
    }, 120);
  });

  if (calm) {
    draw(bolts, 1, REST);
    return;
  }

  var LEADER = 260, FADE = 1100, DELAY = 450;

  target.classList.add('armed');
  setTimeout(function () {
    var began = performance.now();
    var struck = false;

    requestAnimationFrame(function frame(now) {
      var t = now - began;

      if (t < LEADER) {
        draw(bolts, t / LEADER, 1);
      } else {
        if (!struck) {
          struck = true;
          target.classList.remove('armed');
          hero.classList.add('flash');
        }
        var after = (t - LEADER) / FADE;
        var flicker = t - LEADER < 110 ? 0.4 : 1;
        draw(bolts, 1, Math.max(REST, (1 - after) * flicker + REST * after));
        if (after >= 1) { draw(bolts, 1, REST); return; }
      }
      requestAnimationFrame(frame);
    });
  }, DELAY);
})();
