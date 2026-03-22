# Build and Publish

This repository is a WPF app targeting .NET 10. Use the included `publish.ps1` script to produce a small single-file, framework-dependent publish that requires the Windows Desktop runtime to be installed on the target machine.

Examples:

 - Publish x64 Release (default):
   `./publish.ps1 -Runtime win-x64`

 - Publish with ReadyToRun enabled (may increase size):
   `./publish.ps1 -Runtime win-x64 -EnableReadyToRun`

Notes:
 - The publish is framework-dependent (`SelfContained=false`) so users must install the appropriate Windows Desktop runtime.
 - Trimming is enabled by default to reduce file size; test thoroughly as trimming can remove code needed at runtime in some scenarios.
