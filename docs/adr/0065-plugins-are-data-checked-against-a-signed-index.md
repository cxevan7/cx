# ADR-0065: Plugins are data that CX checks against a signed index, never code it runs

Category: Security.

## Status

Accepted 2026-11-27.

## Context

- F1 (foreseen): On 2026-11-25, licensed parts (a GPL model, NVIDIA's DLSS runtime) could not ship in the MIT repository and needed a way in from outside it.
- F2 (foreseen): On 2026-11-25, native plugin systems without a sandbox in other games had spread malicious libraries.

## Decision

While F1 and F2 hold, a plugin is a folder of data: a manifest and a display-space shader stage. CX loads one only when an index signed by a CX plugin key vouches for every file, or in an off-by-default developer mode, and never loads native or managed code from it.

## Consequences

A stage reaches only the frame it is handed, its text is checked before it compiles, and CX downloads nothing. A plugin that needs CPU logic waits for a sandboxed runtime.

## Guard

Plugin.SelfCheck refuses an unsigned or changed plugin outside developer mode, and a stage that declares a buffer, image or sampler.
