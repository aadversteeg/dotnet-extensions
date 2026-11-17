# Reference shaper (tests only)

`harfbuzz.wasm` is HarfBuzz 15.0.0 compiled to WebAssembly, taken unchanged from the npm package harfbuzzjs 1.6.4
(`package/dist/harfbuzz.wasm`, MIT, `LICENSE-harfbuzzjs.txt`; HarfBuzz itself is under the "Old MIT" licence).
SHA-256 `6eeed8e1bc9ad7773038e48ff8f9e07e0ad228572c594b45e5357e9c2f7a0b2e`, 444,394 bytes.

It runs inside the test process on the managed WebAssembly runtime of the NuGet package `WebAssembly` 2.1.0
(Apache-2.0), which compiles WebAssembly to IL: no native code. It is the reference our shaper is compared with
(`HarfBuzzCorpusTests`). No library of the engine references it; the architecture tests check that.
