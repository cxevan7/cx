# Plugins

A plugin is a folder of data CX reads at start: a manifest and one display-space shader stage. It runs no
code on the CPU and links no library (ADR 0065). This page is the format a plugin author writes to.

## Install

Plugins live in a `plugins` folder beside `cx.cfg`, one subfolder per plugin, named after the plugin's id:

    plugins/
      index.txt          one line per vouched file
      index.txt.sig      the index's signature
      vignette/
        plugin.json
        stage.glsl

The player copies a plugin folder in by hand; CX downloads nothing. Each plugin that loads gets its own row
under Graphics > Plugins, off until the player switches it on. `/plugins` (and the Plugins panel) lists what
loaded and why anything was refused.

## Trust

A plugin loads only when `index.txt` is signed by a key built into CX (`packaging/plugin_signers`, under the
`cx-plugins` namespace) and lists the SHA-512 of its `plugin.json` and of its stage, at its exact version. A
maintainer reviews a plugin, adds its lines and signs the index with `tools/plugins/sign-index.sh`.

`plugins.developer` (off) loads unsigned plugins too, for authors testing their own; the panel and `cx.log` mark
each as UNSIGNED.

## plugin.json

Strict JSON: an unknown field, a missing one or a value out of bounds refuses the plugin.

| field | rule |
|---|---|
| `id` | 2 to 24 lowercase letters and digits, a letter first; equals the folder name |
| `version` | `major.minor.patch` |
| `kind` | `shader` |
| `name`, `license` | 1 to 48 printable characters; shown on the plugin's row |
| `stage` | a `.glsl` file name in the plugin's own folder |
| `knobs` | up to 8 sliders, each `key`, `label`, `min`, `max`, `step`, `default`, `uniform` |

A knob's `key` is 1 to 16 lowercase letters and digits; it becomes `plugin_<id>.<key>` in `cx.cfg` and a slider
on the plugin's row. Its `uniform` (`u`, a capital, then letters and digits) is the float the stage reads.

## The stage

The stage defines `vec4 CxStage(vec4 c)`: it takes the colour the stack hands it, display-referred, and returns
the colour to pass on. It may read `vUV` (0..1 across the frame), `uSrc` and `uDepth` (the frame and its
depth, through `texture`), `gl_FragCoord`, and the knob uniforms, which CX declares. It runs after the built-in
grading stages and before anti-aliasing, on Vulkan only.

Refused before compiling: the preprocessor, `layout`, `uniform`, `buffer`, any sampler or image declaration,
`gl_` names other than `gl_FragCoord`, `main`, `while` and `do`, non-ASCII text, over 32 KB. A stage that does
not compile sits the session out.

`examples/plugins/vignette` is a complete plugin.
