# Original notice sources

The files retain their original upstream text. Swift runtime sources use the
official `swift-6.4.0-RELEASE` tag:

- Swift: https://github.com/swiftlang/swift/blob/swift-6.4.0-RELEASE/LICENSE.txt
- corelibs Foundation: https://github.com/swiftlang/swift-corelibs-foundation/blob/swift-6.4.0-RELEASE/LICENSE
- Foundation: https://github.com/swiftlang/swift-foundation/blob/swift-6.4.0-RELEASE/LICENSE.md
- Foundation notices: https://github.com/swiftlang/swift-foundation/blob/swift-6.4.0-RELEASE/NOTICE.txt
- Foundation ICU wrapper: https://github.com/swiftlang/swift-foundation-icu/blob/swift-6.4.0-RELEASE/LICENSE.md
- libdispatch and BlocksRuntime: https://github.com/swiftlang/swift-corelibs-libdispatch/blob/swift-6.4.0-RELEASE/LICENSE

The ICU wrapper's `icuSources/include/_foundation_unicode/uvernum.h` identifies
ICU 76.1. Its original license is retained from
https://github.com/unicode-org/icu/blob/release-76-1/LICENSE.

Swift's release/6.4.0 source configuration pins the remaining runtime dependencies
in `utils/update_checkout/update-checkout-config.json`:

- curl 8.9.1: https://github.com/curl/curl/blob/curl-8_9_1/COPYING
- Brotli 1.2.0: https://github.com/google/brotli/blob/v1.2.0/LICENSE
- zlib 1.3.1: https://github.com/madler/zlib/blob/v1.3.1/LICENSE
- libxml2 2.11.5: https://github.com/gnome/libxml2/blob/v2.11.5/Copyright

The bundle additionally carries licenses from the exact checked-out llama.cpp
revision, Swift Crypto and Swift ASN1 packages. The bundle manifest records
their versions, every source digest and every shipped file digest. Original
redistributable notices found in installed Swift/VC runtime directories are
copied into the same bundle notice directory.
