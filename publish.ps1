Param(
    [ValidateSet("win-x64","win-x86","win-arm64")]
    [string]$Runtime = "win-x64",
    [string]$Configuration = "Release",
    [string]$Output = ".\publish\$Runtime",
    [switch]$EnableTrim,
    [switch]$EnableReadyToRun
)

# Find the first .csproj in the repository root (adjust if you have multiple projects)
$proj = Get-ChildItem -Path . -Filter *.csproj -Recurse | Select-Object -First 1
if (-not $proj) {
    Write-Error "No .csproj found in workspace. Run this script from the repository root."
    exit 1
}

Write-Host "Publishing project:`n  $($proj.FullName)`nConfiguration: $Configuration`nRuntime: $Runtime`nOutput: $Output" -ForegroundColor Cyan

$# Detect whether the project uses WPF; trimming is not supported/recommended for WPF
$projContent = Get-Content -Path $proj.FullName -Raw
$isWpf = $projContent -match '<UseWPF>\s*true\s*</UseWPF>'

if ($isWpf) {
    if ($EnableTrim) {
        Write-Warning "Project appears to use WPF; trimming is not supported for WPF. Ignoring -EnableTrim and publishing without trimming. See https://aka.ms/dotnet-illink/wpf for details."
    }
    $trim = 'false'
} else {
    $trim = 'true' # default enable trimming for smaller size on non-WPF projects
}

$r2r = if ($EnableReadyToRun) { 'true' } else { 'false' } # disable R2R by default to reduce size

dotnet publish $proj.FullName `
    -c $Configuration `
    -r $Runtime `
    -o $Output `
    /p:PublishSingleFile=true `
    /p:SelfContained=false `
    /p:PublishTrimmed=$trim `
    /p:PublishReadyToRun=$r2r `


if ($LASTEXITCODE -eq 0) {
    Write-Host "Publish succeeded. Output folder: $Output" -ForegroundColor Green
} else {
    Write-Error "Publish failed with exit code $LASTEXITCODE"
    exit $LASTEXITCODE
}
