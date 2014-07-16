FFMPEGOnAzureWorkerRoles
========================

Sample how to run simple batch jobs for FFMPEG on Microsoft Azure Cloud Services / Worker Roles / PaaS

### Before 1st compilation

Before compiling this sample, you need to run restore-binaries.cmd, in order to include the latest ffmpeg.exe in your build. Alternatively, you can manually copy ffmpeg.exe into the FFMPEGLib folder. 

# What is this?

This sample demonstrates 

- how to run a simple executable in one or more worker roles in Micorosft Azure, 
- how to submit jobs to this executable, and 
- how to automatically transfer input and output file from Azure blob storage to the executable, and back. 

As example application, I decided to go for FFMPEG, which is a simple app to transform video content. 

