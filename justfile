# RackPeekInventory Development Commands
# Run `just` or `just --list` to see available recipes.

# Variables
# ---------
_dotnet := "dotnet"
_project := "RackPeekInventory/RackPeekInventory.csproj"
_tests := "Tests/Tests.csproj"

# ─── Helpers/Private ────────────────────────────────────────────────────────

[doc("Check if dotnet is installed")]
[private]
_check-dotnet:
    @command -v {{ _dotnet }} >/dev/null 2>&1 || (echo "dotnet not found. Install .NET 10.0 SDK: https://dotnet.microsoft.com/download/dotnet/10.0" && exit 1)

# ─── Default ────────────────────────────────────────────────────────────────

# List all recipes with documentation
[private]
_default:
    @just --list --justfile {{ justfile() }}

# ─── Build ──────────────────────────────────────────────────────────────────

[doc("Build the full solution (Debug)")]
[group("build")]
build: _check-dotnet
    {{ _dotnet }} build

[doc("Build the full solution in Release mode")]
[group("build")]
build-release: _check-dotnet
    {{ _dotnet }} build -c Release

[doc("Publish as self-contained single-file binary")]
[group("build")]
publish runtime="linux-x64": _check-dotnet
    {{ _dotnet }} publish {{ _project }} -c Release -r {{ runtime }} \
        --self-contained true \
        -p:PublishSingleFile=true \
        -o dist/{{ runtime }}

# ─── Test ───────────────────────────────────────────────────────────────────

[doc("Run all tests")]
[group("test")]
test: _check-dotnet
    {{ _dotnet }} test {{ _tests }}

[doc("Run tests with verbose output")]
[group("test")]
test-verbose: _check-dotnet
    {{ _dotnet }} test {{ _tests }} --verbosity normal

# ─── Run ────────────────────────────────────────────────────────────────────

[doc("Dry run — collect and print inventory as JSON (no server needed)")]
[group("run")]
dry-run: _check-dotnet
    {{ _dotnet }} run --project {{ _project }} -- --dry-run

[doc("Dry run with verbose logging")]
[group("run")]
dry-run-verbose: _check-dotnet
    {{ _dotnet }} run --project {{ _project }} -- --dry-run --verbose

[doc("Send inventory to server (requires server-url and api-key)")]
[group("run")]
send server-url api-key: _check-dotnet
    {{ _dotnet }} run --project {{ _project }} -- \
        --Inventory:ServerUrl={{ server-url }} \
        --Inventory:ApiKey={{ api-key }}

[doc("Run in daemon mode (requires server-url and api-key)")]
[group("run")]
daemon server-url api-key interval="300": _check-dotnet
    {{ _dotnet }} run --project {{ _project }} -- --daemon \
        --Inventory:ServerUrl={{ server-url }} \
        --Inventory:ApiKey={{ api-key }} \
        --Inventory:IntervalSeconds={{ interval }}

# ─── Utility ────────────────────────────────────────────────────────────────

[doc("Clean build artifacts (bin, obj)")]
[group("utility")]
clean: _check-dotnet
    {{ _dotnet }} clean
