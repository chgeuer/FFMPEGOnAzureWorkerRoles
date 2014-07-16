$version = "ffmpeg-latest-win64-static"

function Download-File {
	param (
	  [string]$url,
	  [string]$file
	 )
Write-Host "Download $file"
  if (Test-Path $file) 
  {
  	Write-Host "File $file is already there, skipping download"
  	return;
  }
  Write-Host "Downloading $url to $file"
  $downloader = new-object System.Net.WebClient
  $downloader.DownloadFile($url, $file)
}


Download-File "http://ffmpeg.zeranoe.com/builds/win64/static/$($version).7z" "$($version).7z"
Download-File "https://erlang.blob.core.windows.net/installers/7za.exe" "7za.exe"
.\7za.exe x -y "$($version).7z"
Move-Item "$($version)\bin\ffmpeg.exe" FFMPEGLib\ffmpeg.exe
Remove-Item -Recurse -Force ".\$($version)"
