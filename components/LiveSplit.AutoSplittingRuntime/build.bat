cd src\asr-capi
cargo +nightly-x86_64-pc-windows-msvc build -Z trim-paths --config profile.release.trim-paths=true --release --target x86_64-pc-windows-msvc
copy target\x86_64-pc-windows-msvc\release\asr_capi.dll ..\LiveSplit.AutoSplittingRuntime\x64\.
cargo +nightly-i686-pc-windows-msvc build -Z trim-paths --config profile.release.trim-paths=true --release --target i686-pc-windows-msvc
copy target\i686-pc-windows-msvc\release\asr_capi.dll ..\LiveSplit.AutoSplittingRuntime\x86\.
