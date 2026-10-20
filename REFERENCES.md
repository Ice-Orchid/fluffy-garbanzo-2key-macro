# Technical references

Checked 20 October 2026. Public branch links are mutable. The C# implementation
was independently written; no vendor or upstream application code was copied.

## Protocol

The measured device uses 65-byte Windows HID reports, including report ID `03`.
An identification request begins `03 FB FB FB`; the supported reply begins
`03 FB 02 00 00`. Legacy configuration reads send `03 FA 0F 03 <layer>` for
layers 1, 2 and 3. Each returns 24 stored records, including unused templates.
Records are indexed by embedded slot/layer, never arrival order.

Preparation writes F13/F14 (HID usages `68`/`69`) to physical slots 1/2 on
layer 1. Each binding report `03 FE …` is followed by `03 AA AA`,
`03 FD FE FF`, `03 AA AA`. Restore is restricted to those same two records.

- [Exact VID/PID support](https://github.com/kriomant/ch57x-keyboard-tool/pull/188)
- [Device table](https://github.com/kriomant/ch57x-keyboard-tool/blob/master/src/config.rs)
- [k884x encoder and tests](https://github.com/kriomant/ch57x-keyboard-tool/blob/master/src/keyboard/k884x.rs)
- [Other specimen and protocol research](https://github.com/civcode/ch57x-keyboard-tool-cpp/blob/master/doc/documentation.md)
- [Dialect and ordering notes](https://github.com/jvspier/macropad-rebind/blob/main/docs/PROTOCOL.md)

Public descriptions disagree about query fields, identity bytes and ordering.
The fixed `0F 03` read fields do not describe this unit's two physical keys.
Retained local vendor-object evidence and measured replies determined this
implementation's narrow behavior. Device-specific logs and backups are not
published.

## Windows APIs

- [HIDP_CAPS report lengths](https://learn.microsoft.com/en-us/windows-hardware/drivers/ddi/hidpi/ns-hidpi-_hidp_caps)
- [RegisterHotKey](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-registerhotkey)
- [CancelIoEx](https://learn.microsoft.com/en-us/windows/win32/api/ioapiset/nf-ioapiset-cancelioex)
- [.NET Framework FileStream implementation](https://github.com/microsoft/referencesource/blob/main/mscorlib/system/io/filestream.cs)

Output uses a one-byte FileStream buffer so reports bypass managed buffering.
Native handles are retained for cancellation without invoking flushing getters.
Cancellation cannot establish that a report was never transmitted; readback and
recovery checks are still required.

## Vendor archive assessment

Vendor files were inspected as data and were never executed or loaded.
The principal executable and HIDAPI DLL were unsigned. A bounded static review
found normal HID programming imports and no direct main-executable network,
process-launch or registry-write imports. Dynamic loading and bundled runtime
capabilities prevent stronger conclusions: this was not a full behavioral
analysis or malware-free certification.

Vendor binaries, extracted objects and local inspection output are not included.
