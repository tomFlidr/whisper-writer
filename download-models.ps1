# WhisperWriter - Whisper GGML model downloader
# Usage: .\download-models.ps1
# Downloads selected Whisper GGML models from HuggingFace into the models\ folder.

$ErrorActionPreference = "Stop"
$modelsDir = Join-Path $PSScriptRoot "models"
$baseUrl = "https://huggingface.co/ggerganov/whisper.cpp/resolve/main"

$models = @(
	[PSCustomObject]@{ Index = 1;  Name = "large-v3-turbo"; File = "ggml-large-v3-turbo.bin"; Size = "1.6 GB"; Vram = "~3 GB";  Note = "Best speed/accuracy tradeoff" },
	[PSCustomObject]@{ Index = 2;  Name = "large-v3";       File = "ggml-large-v3.bin";       Size = "3.1 GB"; Vram = "~10 GB"; Note = "Most accurate, latest generation" },
	[PSCustomObject]@{ Index = 3;  Name = "large-v2";       File = "ggml-large-v2.bin";       Size = "3.1 GB"; Vram = "~10 GB"; Note = "Most accurate, recommended (default)" },
	[PSCustomObject]@{ Index = 4;  Name = "large-v1";       File = "ggml-large-v1.bin";       Size = "3.1 GB"; Vram = "~10 GB"; Note = "Accurate, older generation" },
	[PSCustomObject]@{ Index = 5;  Name = "medium";         File = "ggml-medium.bin";         Size = "1.5 GB"; Vram = "~5 GB";  Note = "Good balance, multilingual" },
	[PSCustomObject]@{ Index = 6;  Name = "medium.en";      File = "ggml-medium.en.bin";      Size = "1.5 GB"; Vram = "~5 GB";  Note = "Good balance, English only" },
	[PSCustomObject]@{ Index = 7;  Name = "small";          File = "ggml-small.bin";          Size = "488 MB"; Vram = "~2 GB";  Note = "Fast, multilingual" },
	[PSCustomObject]@{ Index = 8;  Name = "small.en";       File = "ggml-small.en.bin";       Size = "488 MB"; Vram = "~2 GB";  Note = "Fast, English only" },
	[PSCustomObject]@{ Index = 9;  Name = "base";           File = "ggml-base.bin";           Size = "148 MB"; Vram = "~1 GB";  Note = "Very fast, multilingual" },
	[PSCustomObject]@{ Index = 10; Name = "base.en";        File = "ggml-base.en.bin";        Size = "148 MB"; Vram = "~1 GB";  Note = "Very fast, English only" },
	[PSCustomObject]@{ Index = 11; Name = "tiny";           File = "ggml-tiny.bin";           Size = "78 MB";  Vram = "~390 MB"; Note = "Fastest, multilingual, least accurate" },
	[PSCustomObject]@{ Index = 12; Name = "tiny.en";        File = "ggml-tiny.en.bin";        Size = "78 MB";  Vram = "~390 MB"; Note = "Fastest, English only, least accurate" }
)

function Write-Header {
	Write-Host ""
	Write-Host "=============================================" -ForegroundColor Cyan
	Write-Host "   WhisperWriter - Whisper Model Downloader  " -ForegroundColor Cyan
	Write-Host "=============================================" -ForegroundColor Cyan
	Write-Host ""
}

function Write-ModelTable {
	Write-Host ("  {0,-4} {1,-20} {2,-10} {3,-10} {4}" -f "#", "Model", "Disk", "VRAM", "Notes") -ForegroundColor White
	Write-Host ("  {0,-4} {1,-20} {2,-10} {3,-10} {4}" -f "---", "-------------------", "--------", "--------", "-----") -ForegroundColor DarkGray

	foreach ($m in $models) {
		$destFile = Join-Path $modelsDir $m.File
		if (Test-Path $destFile) {
			$status = " [downloaded]"
			$color  = "Green"
		} else {
			$status = ""
			$color  = "White"
		}
		Write-Host ("  {0,-4} {1,-20} {2,-10} {3,-10} {4}{5}" -f $m.Index, $m.Name, $m.Size, $m.Vram, $m.Note, $status) -ForegroundColor $color
	}
	Write-Host ""
}

function Download-Model($m) {
	$destFile = Join-Path $modelsDir $m.File
	if (Test-Path $destFile) {
		Write-Host "  [SKIP] $($m.File) already exists." -ForegroundColor DarkGray
		return
	}

	$url = "$baseUrl/$($m.File)"
	Write-Host ""
	Write-Host "  Downloading $($m.Name) ($($m.Size)) ..." -ForegroundColor Yellow
	Write-Host "  Source : $url" -ForegroundColor DarkGray
	Write-Host "  Target : $destFile" -ForegroundColor DarkGray
	Write-Host ""

	$tmpFile = "$destFile.tmp"
	try {
		$client = [System.Net.WebClient]::new()
		$task = $client.DownloadFileTaskAsync($url, $tmpFile)

		while (-not $task.IsCompleted) {
			Start-Sleep -Milliseconds 300
			if (Test-Path $tmpFile) {
				$received = [math]::Round((Get-Item $tmpFile).Length / 1MB, 1)
				Write-Host "`r  Downloaded: $received MB   " -NoNewline -ForegroundColor Cyan
			}
		}
		$client.Dispose()

		if ($task.IsFaulted) {
			throw $task.Exception.InnerException
		}

		Write-Host ""
		Move-Item -Path $tmpFile -Destination $destFile -Force
		Write-Host "  [OK] $($m.File) downloaded successfully." -ForegroundColor Green
	} catch {
		Write-Host ""
		Write-Host "  [ERROR] Download failed: $_" -ForegroundColor Red
		if (Test-Path $tmpFile) { Remove-Item $tmpFile -Force }
	}
}

function Parse-Selection($raw, $count) {
$selected = @()
foreach ($part in $raw -split "[,\s]+") {
		$part = $part.Trim()
		if ($part -match "^(\d+)-(\d+)$") {
			$from = [int]$Matches[1]
			$to   = [int]$Matches[2]
			for ($i = [math]::Min($from, $to); $i -le [math]::Max($from, $to); $i++) {
				if ($i -ge 1 -and $i -le $count) { $selected += $i }
			}
		} elseif ($part -match "^\d+$") {
			$n = [int]$part
			if ($n -ge 1 -and $n -le $count) { $selected += $n }
		}
	}
	return ($selected | Select-Object -Unique | Sort-Object)
}

# --- main ---

if (-not (Test-Path $modelsDir)) {
	New-Item -ItemType Directory -Path $modelsDir | Out-Null
	Write-Host "  Created directory: $modelsDir" -ForegroundColor DarkGray
}

Write-Header
Write-Host "  Models will be saved to:" -ForegroundColor Gray
Write-Host "  $modelsDir" -ForegroundColor Gray
Write-Host ""
Write-ModelTable

Write-Host "  Enter the numbers of models to download." -ForegroundColor White
Write-Host "  Separate multiple numbers with commas or spaces. Ranges are supported (e.g. 1-3,7)." -ForegroundColor DarkGray
Write-Host "  Press ENTER without input to exit." -ForegroundColor DarkGray
Write-Host ""
$raw = Read-Host "  Your selection"

if ([string]::IsNullOrWhiteSpace($raw)) {
	Write-Host ""
	Write-Host "  No selection. Exiting." -ForegroundColor DarkGray
	Write-Host ""
	exit 0
}

$indices = Parse-Selection $raw $models.Count
if ($indices.Count -eq 0) {
	Write-Host ""
	Write-Host "  No valid numbers recognized. Exiting." -ForegroundColor Red
	Write-Host ""
	exit 1
}

Write-Host ""
Write-Host "  Selected models:" -ForegroundColor White
foreach ($i in $indices) {
	$m = $models | Where-Object { $_.Index -eq $i }
	Write-Host "    - $($m.Name)  ($($m.Size))" -ForegroundColor Cyan
}
Write-Host ""
$confirm = Read-Host "  Proceed with download? [Y/n]"
if ($confirm -match "^[Nn]") {
	Write-Host ""
	Write-Host "  Cancelled." -ForegroundColor DarkGray
	Write-Host ""
	exit 0
}

foreach ($i in $indices) {
	$m = $models | Where-Object { $_.Index -eq $i }
	Download-Model $m
}

Write-Host ""
Write-Host "  Done." -ForegroundColor Green
Write-Host ""
