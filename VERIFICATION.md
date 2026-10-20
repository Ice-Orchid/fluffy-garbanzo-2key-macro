# Verification

Initial local verification: 20 October 2026. This is a personal keypad utility,
not a Galatea release or hardware certification.

- 21 protocol checks, 18 hardware transaction checks and 13 launcher checks
  passed, along with a native form harness.
- Both GUI and console binaries built using Windows' installed .NET Framework
  compiler. The publication copy uses the current user's Documents path instead
  of embedding the original operator's Windows username.
- Separate reviewers inspected hardware programming/recovery and launcher/UI
  behavior. Corrupt-backup handling and buffered-write replay findings were
  fixed and re-reviewed before programming the device.
- Native layouts were inspected at normal and minimum size. Labelled controls,
  saving assignments and switching physical-test mode were exercised.
- The physical device was backed up using two identical complete 72-record
  snapshots. Only layer 1, slots 1/2 changed from A/B to F13/F14; the other 70
  stored records were unchanged.
- The operator reported pressing each key before and after unplugging, but the
  initial log contained only one pair. The missing pair remains unexplained.
  A separate requested test after the reported reconnect recorded both keys,
  and fresh configuration readback still contained F13/F14.
- With target launching enabled, the operator confirmed both Galatea Talk and
  Stop work. This is operator-reported end-to-end success.

The HID protocol was physically exercised on one two-button specimen, not every
keyboard with this VID/PID. Hardware restoration and interrupted-write recovery
have simulated coverage but have not been deliberately exercised on that unit.

Still unverified: Stop during Talk startup, held-button repeat suppression on
the physical device, Windows sign-in/reboot, screen-reader speech and sustained
use. Optional startup was left off during initial verification.

The original local evidence and backup are retained outside this repository.
Do not infer long-term reliability, universal compatibility or Galatea release
approval from these results.
