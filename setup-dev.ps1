# WhisperWriter - Developer environment setup
# Run once after cloning the repository.
# Checks prerequisites, restores NuGet packages, copies CUDA runtime DLLs, and optionally downloads a Whisper model.

$ErrorActionPreference = "Stop"
$root = $PSScriptRoot

# ---------------------------------------------------------------------------
# Helpers
# ---------------------------------------------------------------------------
function Write-Header {
	Write-Host ""
	Write-Host "=============================================" -ForegroundColor Cyan
	Write-Host "   WhisperWriter - Developer Setup           " -ForegroundColor Cyan
	Write-Host "=============================================" -ForegroundColor Cyan
	Write-Host ""
}

function Write-Step([string]$text) {
	Write-Host ""
	Write-Host ">> $text" -ForegroundColor Yellow
}

function Write-Ok([string]$text) {
	Write-Host "   OK  $text" -ForegroundColor Green
}

function Write-Warn([string]$text) {
	Write-Host "   WARN  $text" -ForegroundColor DarkYellow
}

function Write-Fail([string]$text) {
	Write-Host "   FAIL  $text" -ForegroundColor Red
}

# ---------------------------------------------------------------------------
# Step 1 – .NET 8 SDK
# ---------------------------------------------------------------------------
Write-Header

Write-Step "Checking .NET 8 SDK..."

$dotnetOk = $false
try {
	$sdks = & dotnet --list-sdks 2>&1
	# Accept any SDK >= 8 (newer SDKs can build net8.0-windows targets)
	$compatibleSdks = $sdks | Where-Object { $_ -match "^([89]|\d{2,})\." }
	if ($compatibleSdks) {
		Write-Ok ".NET SDK (>= 8) found:"
		$compatibleSdks | ForEach-Object { Write-Host "       $_" -ForegroundColor Gray }
		$dotnetOk = $true
	} else {
		Write-Fail "No compatible .NET SDK (>= 8) found. Installed SDKs:"
		$sdks | ForEach-Object { Write-Host "       $_" -ForegroundColor Gray }
		Write-Host ""
		Write-Host "   Download from: https://dotnet.microsoft.com/download/dotnet/8.0" -ForegroundColor White
	}
} catch {
	Write-Fail "'dotnet' command not found in PATH."
	Write-Host "   Download from: https://dotnet.microsoft.com/download/dotnet/8.0" -ForegroundColor White
}

if (-not $dotnetOk) {
	Write-Host ""
	Write-Host "Setup cannot continue without a compatible .NET SDK. Aborting." -ForegroundColor Red
	exit 1
}

# ---------------------------------------------------------------------------
# Step 2 – CUDA Toolkit detection
# ---------------------------------------------------------------------------
Write-Step "Detecting NVIDIA CUDA Toolkit..."

# Supported CUDA major versions (newest first)
$supportedMajors = @(13, 12, 11)

$cudaFound      = $false
$cudaBinDir     = $null
$cudaVersion    = $null

# Strategy A: environment variable CUDA_PATH or CUDA_PATH_Vx_y
$cudaCandidates = @()
foreach ($major in $supportedMajors) {
	$envVars = [System.Environment]::GetEnvironmentVariables("Machine").GetEnumerator() |
		Where-Object { $_.Key -match "^CUDA_PATH(_V\d+_\d+)?$" }
	foreach ($ev in $envVars) {
		$p = Join-Path $ev.Value "bin\x64"
		if (Test-Path $p) { $cudaCandidates += $p }
	}
}

# Strategy B: standard install paths
foreach ($major in $supportedMajors) {
	$base = "C:\Program Files\NVIDIA GPU Computing Toolkit\CUDA"
	if (Test-Path $base) {
		Get-ChildItem $base -Directory | Where-Object { $_.Name -match "^v($major)\." } | ForEach-Object {
			$p = Join-Path $_.FullName "bin\x64"
			if (Test-Path $p) { $cudaCandidates += $p }
		}
	}
}

# Strategy C: nvcc in PATH
try {
	$nvccPath = (Get-Command nvcc -ErrorAction SilentlyContinue).Source
	if ($nvccPath) {
		$p = Split-Path $nvccPath -Parent
		if ($p -notin $cudaCandidates) { $cudaCandidates += $p }
	}
} catch {}

# Pick first candidate that has cudart64_*.dll
foreach ($candidate in $cudaCandidates) {
	$cudartDll = Get-ChildItem $candidate -Filter "cudart64_*.dll" -ErrorAction SilentlyContinue | Select-Object -First 1
	if ($cudartDll) {
		$cudaBinDir  = $candidate
		# Extract version number from filename, e.g. cudart64_13.dll -> 13
		if ($cudartDll.Name -match "cudart64_(\d+)\.dll") { $cudaVersion = $matches[1] }
		$cudaFound = $true
		break
	}
}

if ($cudaFound) {
	Write-Ok "CUDA Toolkit found (runtime version $cudaVersion):"
	Write-Host "       $cudaBinDir" -ForegroundColor Gray
} else {
	Write-Warn "CUDA Toolkit not found. GPU acceleration will not be available."
	Write-Host "   Download from: https://developer.nvidia.com/cuda-downloads" -ForegroundColor White
	Write-Host "   The application will still work using CPU inference." -ForegroundColor White
}

# ---------------------------------------------------------------------------
# Step 3 – NuGet restore
# ---------------------------------------------------------------------------
Write-Step "Restoring NuGet packages..."

$csproj = Join-Path $root "WhisperWriter.csproj"
try {
	& dotnet restore $csproj --verbosity quiet
	Write-Ok "NuGet packages restored."
} catch {
	Write-Fail "NuGet restore failed: $_"
	exit 1
}

# ---------------------------------------------------------------------------
# Step 4 – Copy CUDA runtime DLLs to runtimes\cuda\win-x64
# ---------------------------------------------------------------------------
if ($cudaFound) {
	Write-Step "Copying CUDA runtime DLLs..."

	# Determine which DLL names to copy based on detected major version
	$cudaDlls = @(
		"cudart64_$cudaVersion.dll",
		"cublas64_$cudaVersion.dll",
		"cublasLt64_$cudaVersion.dll"
	)

	$configs = @("Debug", "Release")
	foreach ($cfg in $configs) {
		$targetDir = Join-Path $root "bin\$cfg\net8.0-windows\runtimes\cuda\win-x64"
		if (-not (Test-Path $targetDir)) {
			# bin may not exist yet before the first build – that is fine, MSBuild copies on build
			Write-Warn "Output folder not found (not built yet): bin\$cfg - skipping DLL copy for $cfg."
			continue
		}

		$allCopied = $true
		foreach ($dllName in $cudaDlls) {
			$src = Join-Path $cudaBinDir $dllName
			$dst = Join-Path $targetDir $dllName
			if (-not (Test-Path $src)) {
				Write-Warn "Source DLL not found, skipping: $src"
				$allCopied = $false
				continue
			}
			try {
				Copy-Item $src $dst -Force
			} catch {
				Write-Warn "Could not copy ${dllName}: $_"
				$allCopied = $false
			}
		}

		if ($allCopied) {
			Write-Ok "CUDA DLLs copied to bin\$cfg."
		}
	}

	# Also update the hardcoded CUDA path in the .csproj if it differs from what we found
	Write-Step "Checking CopyCudaRuntimeDlls path in WhisperWriter.csproj..."
	$csprojContent = Get-Content $csproj -Encoding UTF8 -Raw
	$currentCudaBinDir = $null
	if ($csprojContent -match '<CudaBinDir>(.*?)</CudaBinDir>') {
		$currentCudaBinDir = $matches[1].Trim()
	}

	if ($currentCudaBinDir -and ($currentCudaBinDir -ne $cudaBinDir)) {
		Write-Warn "CudaBinDir in .csproj differs from detected path."
		Write-Host "   .csproj : $currentCudaBinDir" -ForegroundColor Gray
		Write-Host "   Detected: $cudaBinDir" -ForegroundColor Gray
		$answer = Read-Host "   Update .csproj to use the detected path? [Y/N]"
		if ($answer -match "^[Yy]") {
			$updated = $csprojContent -replace [regex]::Escape($currentCudaBinDir), $cudaBinDir
			[System.IO.File]::WriteAllText($csproj, $updated, [System.Text.Encoding]::UTF8)
			Write-Ok ".csproj updated."
		} else {
		Write-Warn "Skipped - .csproj not modified. Build may not copy CUDA DLLs correctly."
		}
	} elseif ($null -ne $currentCudaBinDir) {
		Write-Ok "CudaBinDir in .csproj already matches detected path."
	}
}

# ---------------------------------------------------------------------------
# Step 5 – Whisper model check
# ---------------------------------------------------------------------------
Write-Step "Checking Whisper models..."

$modelsDir   = Join-Path $root "llms"
$defaultModel = "ggml-medium.bin"
$defaultPath  = Join-Path $modelsDir $defaultModel

if (Test-Path $defaultPath) {
	Write-Ok "Default model found: $defaultModel"
} else {
	$existingModels = @()
	if (Test-Path $modelsDir) {
		$existingModels = Get-ChildItem $modelsDir -Filter "ggml-*.bin" | Select-Object -ExpandProperty Name
	}

	if ($existingModels.Count -gt 0) {
		Write-Warn "Default model ($defaultModel) not found, but the following models are present:"
		$existingModels | ForEach-Object { Write-Host "       $_" -ForegroundColor Gray }
		Write-Host "   The application will use the first available model or download medium on first run." -ForegroundColor White
	} else {
		Write-Warn "No Whisper models found in models\"
		Write-Host ""
		$answer = Read-Host "   Download the default model (ggml-large-v2, ~3.1 GB) now? [Y/N]"
		if ($answer -match "^[Yy]") {
			& "$root\download-models.ps1"
		} else {
			Write-Host "   You can download models later via download-models.bat" -ForegroundColor White
			Write-Host "   The application will download the medium model automatically on first run." -ForegroundColor White
		}
	}
}

# ---------------------------------------------------------------------------
# Done
# ---------------------------------------------------------------------------
Write-Host ""
Write-Host "=============================================" -ForegroundColor Cyan
Write-Host "   Setup complete!                           " -ForegroundColor Cyan
Write-Host "=============================================" -ForegroundColor Cyan
Write-Host ""
Write-Host "  Next steps:" -ForegroundColor White
Write-Host "    1. Open WhisperWriter.sln in Visual Studio 2022" -ForegroundColor Gray
Write-Host "    2. Build the solution (Ctrl+Shift+B)" -ForegroundColor Gray
Write-Host "    3. Run the application (F5)" -ForegroundColor Gray
Write-Host ""
if (-not $cudaFound) {
	Write-Host "  NOTE: CUDA was not found. The app runs on CPU (slower transcription)." -ForegroundColor DarkYellow
	Write-Host "        Install CUDA Toolkit from https://developer.nvidia.com/cuda-downloads" -ForegroundColor DarkYellow
	Write-Host "        and re-run setup-dev.bat to enable GPU acceleration." -ForegroundColor DarkYellow
	Write-Host ""
}
