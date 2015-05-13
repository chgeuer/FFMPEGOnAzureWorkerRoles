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

.\bin\7z.exe x -y "$($version).7z"

$folder = (Get-ChildItem -Recurse -Filter "ffmpeg-*-win64-static").FullName
Remove-Item FFMPEGLib\ffmpeg.exe
Copy-Item "$($folder)\bin\ffmpeg.exe" FFMPEGLib\ffmpeg.exe
Remove-Item -Recurse -Force "$($folder)"
