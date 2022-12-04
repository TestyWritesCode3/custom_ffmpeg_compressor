﻿/*
 * BRIEFING
	This program encodes videos of any extension supported by ffmpeg to HEVC in mp4.
	It uses settings defined in a settings.json file, which is created if it doesn't exist.
	The settings.json file is used to define the source and destination folders, the CQP value, and other settings.
	The program will encode all videos in the source folder, unless they are set to be ignored in the settings.json file.
	If the videos are encoded successfully, the source video will be deleted. The encoded video is then copied and moved to the destination folder.
	A log file will also be created in a folder called "_logs" in the program's folder.
	The log file will contain the output of the ffmpeg command, as well as the settings used to encode each video.
	It will also contain the time it took to encode each video, the size of the video before and after encoding, and the date and time the video was encoded.
*/

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Configuration;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using WMPLib;

namespace custom_ffmpeg_compressor
{
	internal class hevc_ffmpeg
	{
		static string _ver = "rev6";
		static string currentDirectory = Directory.GetCurrentDirectory();

		static CompressorSettings settings = new CompressorSettings();
		static string[] files = { };

		static LogManager.LogFile logFileForFile;


		static bool shouldEncode = true;

		static void Main(string[] args)
		{
			// [PREPARATION] //

			LogManager.Init();
			FileManager.Init();

			LogManager.processLog.Log(string.Format("Preparing {0} module ({1})...", nameof(hevc_ffmpeg), _ver));
			

			// ---[READ SETTINGS]--- //
			settings = RetrieveSettings();

			files = Directory.GetFiles(settings.sourceFolder);

			// Create the ffmpeg command
			string command = "ffmpeg -hwaccel_output_format cuda -i \"{0}\" -map 0:v -map 0:a -map_metadata 0 -c:v hevc_nvenc -rc constqp -qp " + settings.cqp + " -b:v 0K -c:a copy -movflags +faststart -movflags use_metadata_tags -y \"{1}\"";

			LogManager.processLog.Log("Using ffmpeg command: " + command);
			
			
			
			// ---[ENCODE FILES]--- //
			foreach (string file in files)
			{
				if (!shouldEncode)
				{
					break;
				}

				string fileName = Path.GetFileName(file);

				
				string divider = string.Format("-----[{0}]-----", fileName);

				LogManager.processLog.LogBreak();
				LogManager.processLog.Log(divider, false);
				LogManager.processLog.Log(string.Format("Loaded file {0}", fileName));


				// Check if the file is in the list of ignored files
				if (!settings.ignoredFiles.Contains(fileName))
				{
					// Create a log file for the file in the default logs folder
					logFileForFile = new LogManager.LogFile(LogManager.logFolderPath + Array.IndexOf(files, file).ToString(), DateTime.MaxValue);

					logFileForFile.Log(divider, false);


					string fileNameWithoutExtension = Path.GetFileNameWithoutExtension(file);


					// Get the file size before encoding
					long fileSize = new FileInfo(file).Length;
					double fileSizeMB = fileSize / 1000000.0;

					// Write the file size to the log files
					string fileSizeLogText = string.Format("File size: {0} MB", Math.Round(fileSizeMB, 2));

					LogManager.LogWithProcessLog(divider, logFileForFile, false);
					LogManager.LogWithProcessLog(fileSizeLogText, logFileForFile, false);

					// Log prediction of compression time (assume an average of 360fps)
					double compressionTime = fileSizeMB / 360.0;
					LogManager.LogWithProcessLog(string.Format("Estimated compression time: {0} seconds", Math.Round(compressionTime, 2)), logFileForFile, false);



					// ---[COMPRESS FILE]--- //
					string encodedFileName = CompressFile(file, command);

					// ---[COMPARE FILES]--- //
					CompareFiles(file, encodedFileName);

					// Log that the file was processed
					LogManager.processLog.LogBreak();

					LogManager.LogWithProcessLog("File has finished processing", logFileForFile);
					LogManager.processLog.LogBreak();

				}
				else
				{
					// Write that the file is in the list of ignored files to the process log file
					LogManager.processLog.LogBreak();
					LogManager.processLog.Log(string.Format("File {0} is in ignored files list. Skipping...", fileName));
					LogManager.processLog.LogBreak();
				}
			}

			// [FINISH PROCESSING] //
			LogManager.processLog.LogBreak();
			LogManager.processLog.Log("All files have finished processing.");
			LogManager.processLog.LogBreak();

			Console.WriteLine("Program has finished");
		}


		// [FUNCTIONS] //






		///<summary>
		///Compares the original and encoded file, and modifies them accordingly.
		///</summary>
		static void CompareFiles(string original, string encoded)
		{
			LogManager.LogWithProcessLog("FILE COMPARISON", logFileForFile);
			LogManager.processLog.Indent();
			logFileForFile.Indent();




			// [LENGTH COMPARISON] //


			// Get original file length
			WindowsMediaPlayer wmp = new WindowsMediaPlayerClass();
			IWMPMedia mediainfo = wmp.newMedia(original);
			var originalDuration = mediainfo.duration;

			// Get encoded file length
			mediainfo = wmp.newMedia(encoded);
			var encodedDuration = mediainfo.duration;
			wmp.close();

			// Compare the lengths
			double lenDifference = Math.Abs(originalDuration - encodedDuration);
			LogManager.LogWithProcessLog("Length difference: " + lenDifference + " seconds", logFileForFile, false);
			if (lenDifference != 0)
			{
				LogManager.LogWithProcessLog("Failed to complete encoding. Dropping all other files.", logFileForFile, true, LogManager.LogLevelEnum.Error);
				shouldEncode = false;
				return;
			}

			// [SIZE COMPARISON] //

			// Get original file size
			long originalFileSize = new FileInfo(original).Length;
			double originalFileSizeMB = originalFileSize / 1000000.0;

			// Get encoded file size
			long encodedFileSize = new FileInfo(encoded).Length;
			double encodedFileSizeMB = encodedFileSize / 1000000.0;

			// Write the file sizes to the log files
			LogManager.LogWithProcessLog("Original file size: " + Math.Round(originalFileSizeMB, 2) + " MB", logFileForFile, false);
			LogManager.LogWithProcessLog("Encoded file size: " + Math.Round(encodedFileSizeMB, 2) + " MB", logFileForFile, false);

			// Calculate the difference in file size
			double sizeDifference = Math.Abs(originalFileSizeMB - encodedFileSizeMB);
			LogManager.LogWithProcessLog("Size difference: " + Math.Round(sizeDifference, 2) + " MB", logFileForFile, false);

			// Calculate the percentage of the original file size that the encoded file is
			double percentage = (encodedFileSizeMB / originalFileSizeMB) * 100;
			LogManager.LogWithProcessLog("Percentage of original file size: " + Math.Round(percentage, 2) + "%", logFileForFile, false);

			LogManager.processLog.Unindent();
			logFileForFile.Unindent();
			LogManager.LogWithProcessLog("FILE MANAGEMENT", logFileForFile);
			LogManager.processLog.Indent();
			logFileForFile.Indent();

			// [FILE MANAGEMENT] //

			// Compare the file sizes
			if (encodedFileSize < originalFileSize)
			{
				// [ENCODED FILE IS SMALLER] //
				LogManager.LogWithProcessLog("Encoded file is smaller than the original file", logFileForFile, false);

				if (settings.deleteSource)
				{
					// Delete the original file
					bool delSuccess = FileManager.DeleteFile(original,settings.deletePermanently);
					if (delSuccess) LogManager.LogWithProcessLog("Deleted original file", logFileForFile);
					else LogManager.LogWithProcessLog("Failed to delete original file", logFileForFile);
				}

				string encodedFileNameWithoutExtension = Path.GetFileNameWithoutExtension(encoded);
				string encodedFileCopyName = string.Format("{0}{1}_TEMP{2}", settings.sourceFolder, encodedFileNameWithoutExtension, Path.GetExtension(encoded));

				// Copy the encoded file to the same folder, with the suffix "TEMP"
				FileManager.CopyFile(encoded, encodedFileCopyName, false);

				// Move the copy of the encoded file to the destination folder
				string encodedFileDestinationName = settings.destinationFolder + "\\" + encodedFileNameWithoutExtension + ".mp4";
				bool moveSuccess = FileManager.MoveFile(encodedFileCopyName, encodedFileDestinationName, false);
				
				// Check that the move was successfull
				if (moveSuccess)
				{
					LogManager.LogWithProcessLog("Moved encoded file to destination folder", logFileForFile);

					// Delete the original encoded file
					bool delSuccess = FileManager.DeleteFile(encoded,settings.deletePermanently);

					if (delSuccess) LogManager.LogWithProcessLog("Deleted encoded file copy", logFileForFile);
					else LogManager.LogWithProcessLog("Failed to delete encoded file copy", logFileForFile);
				}
				else
				{
					LogManager.LogWithProcessLog("Failed to move encoded file to destination folder. Assuming destination drive is full.", logFileForFile);
					settings.deleteSource = false;
				}
			}
			else
			{
				// [ENCODED FILE IS LARGER] //
				LogManager.LogWithProcessLog("Encoded file is larger than the original file", logFileForFile, false);

				// Delete the encoded file
				bool delSuccess = FileManager.DeleteFile(encoded, settings.deletePermanently);

				if (delSuccess) LogManager.LogWithProcessLog("Deleted encoded file", logFileForFile);
				else LogManager.LogWithProcessLog("Failed to delete encoded file", logFileForFile);
			}

			// [FINISH COMPARISON] //
			LogManager.LogWithProcessLog("File comparison finished", logFileForFile);
			LogManager.processLog.Unindent();
			logFileForFile.Unindent();
		}




		///<summary>
		///Runs the given ffmpeg command on the given file.
		///</summary>
		static string CompressFile(string fileName, string command)
		{
			string fileNameWithoutExtension = Path.GetFileNameWithoutExtension(fileName);

			// Create a new process
			string encodedFileName = string.Format("{0}\\{1}_{2}.mp4", settings.sourceFolder, fileNameWithoutExtension, settings.suffix);
			string processCommand = string.Format(command, fileName, encodedFileName);

			Process process = new Process();
			int exitCode = 0;
			process.StartInfo.CreateNoWindow = !settings.showProcessWindow;
			process.StartInfo.UseShellExecute = false;
			//process.StartInfo.RedirectStandardOutput = true;
			process.StartInfo.FileName = "cmd.exe";
			process.StartInfo.Arguments = "/C " + processCommand;

			LogManager.processLog.Log("Started compression of file [" + fileName + "]");
			process.Start();


			LogManager.LogWithProcessLog("--- PROCESS OUTPUT ---", logFileForFile);
			// Log the output stream while the process is running
			/*
			while (!process.StandardOutput.EndOfStream)
			{
				string line = process.StandardOutput.ReadLine();
				LogManager.WriteToLogs(processAndCurrentFileLogs, line, true);
			}
			*/

			process.WaitForExit();

			LogManager.LogWithProcessLog("File comparison finished", logFileForFile);
			LogManager.processLog.LogBreak();
			LogManager.LogWithProcessLog("--- PROCESS OUTPUT ---", logFileForFile);

			exitCode = process.ExitCode;

			process.Close();
			LogManager.processLog.Log(string.Format("Finished compression of file [{0}] with exit code {1}", fileName, exitCode));

			return encodedFileName;

		}









		static CompressorSettings RetrieveSettings()
		{
			// Get the settings from the settings.settings file
			CompressorSettings settings = new CompressorSettings();

			LogManager.processLog.Log("Successfully retrieved settings");
			settings.logSettings();
			
			return settings;
		}
	}
}
