﻿using Microsoft.ProjectOxford.Face;
using Microsoft.ProjectOxford.Face.Contract;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Text;
using System.Threading.Tasks;
using Windows.Devices.Enumeration;
using Windows.Foundation;
using Windows.Foundation.Collections;
using Windows.Graphics.Imaging;
using Windows.Media;
using Windows.Media.Capture;
using Windows.Media.MediaProperties;
using Windows.Storage;
using Windows.Storage.Streams;
using Windows.UI.Popups;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;
using Windows.UI.Xaml.Data;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Media.Imaging;
using Windows.UI.Xaml.Navigation;

namespace FaceApiDemo
{
    public sealed partial class MainPage : Page
    {
        private const string FaceApiKey = "234e6e305e2e49febfe835a85e69a157";
        private const int ControlLoopDelayMilliseconds = 5000; // Update the CountdownStoryboard as well!

        private MediaCapture mediaCapture;

        public MainPage()
        {
            this.InitializeComponent();
        }

        protected override async void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);

#if !DEBUG
            try
            {
#endif
            await StartPreviewAsync();
            await RunControlLoopAsync();
#if !DEBUG
            }
            catch (Exception ex)
            {
            await new MessageDialog(ex.ToString()).ShowAsync();
            Application.Current.Exit();
            }
#endif
        }

        private async Task StartPreviewAsync()
        {
            await UpdateStatusAsync("Initializing preview video feed...");

            // Attempt to get the front camera if one is available, but use any camera device if not
            DeviceInformation cameraDevice;
            var allVideoDevices = await DeviceInformation.FindAllAsync(DeviceClass.VideoCapture);
            DeviceInformation desiredDevice = allVideoDevices.FirstOrDefault(x => x.EnclosureLocation != null && x.EnclosureLocation.Panel == Windows.Devices.Enumeration.Panel.Front);
            cameraDevice = desiredDevice ?? allVideoDevices.FirstOrDefault();
            if (cameraDevice == null) throw new Exception("No camera found on device");

            // Create MediaCapture and its settings, then start the view preview
            mediaCapture = new MediaCapture();
            var settings = new MediaCaptureInitializationSettings { VideoDeviceId = cameraDevice.Id, StreamingCaptureMode = StreamingCaptureMode.Video };
            await mediaCapture.InitializeAsync(settings);
            PreviewCaptureElement.Source = mediaCapture;
            await mediaCapture.StartPreviewAsync();
        }

        /// <summary>
        /// This is an infinite loop which
        /// takes a picture with the attached camera,
        /// displays it,
        /// sends it for recognition to the Microsoft Project Oxford Face API,
        /// displays recognition results overlaid on the picture,
        /// waits for 5 seconds to allow the result to be examined,
        /// starts over.
        /// </summary>
        private async Task RunControlLoopAsync()
        {
            while (true)
            {
                // Take camera picture
                await UpdateStatusAsync("Taking still picture...");

                // TODO focus if possible
                //await mediaCapture.VideoDeviceController.FocusControl.FocusAsync();
                CameraFlashStoryboard.Begin();
                CountdownProgressBar.Value = 100;
                var previewProperties = mediaCapture.VideoDeviceController.GetMediaStreamProperties(MediaStreamType.VideoPreview) as VideoEncodingProperties;
                var videoFrame = new VideoFrame(BitmapPixelFormat.Bgra8, (int)previewProperties.Width, (int)previewProperties.Height);
                var capturedFrame = await mediaCapture.GetPreviewFrameAsync(videoFrame);

                // Display camera picture
                await UpdateStatusAsync("Displaying sample picture...");

                SoftwareBitmap softwareBitmap = capturedFrame.SoftwareBitmap;
                WriteableBitmap writeableBitmap = new WriteableBitmap(softwareBitmap.PixelWidth, softwareBitmap.PixelHeight);
                softwareBitmap.CopyToBuffer(writeableBitmap.PixelBuffer);
                ResultImage.Source = writeableBitmap;

                // Send picture for recognition
                // We need to encode the raw image as a JPEG to make sure the service can recognize it.
                // TODO use a MemoryStream instead of a file
                await UpdateStatusAsync("Uploading picture to Microsoft Project Oxford Face API...");

                var tempImageFile = await ApplicationData.Current.TemporaryFolder.CreateFileAsync("FaceApiDemoFile.jpg", CreationCollisionOption.ReplaceExisting);
                using (var stream = await tempImageFile.OpenAsync(FileAccessMode.ReadWrite))
                {
                    var bitmapEncoder = await BitmapEncoder.CreateAsync(BitmapEncoder.JpegEncoderId, stream);
                    bitmapEncoder.SetSoftwareBitmap(softwareBitmap);
                    await bitmapEncoder.FlushAsync();
                }
                Face[] recognizedFaces;
                using (var stream = await tempImageFile.OpenStreamForReadAsync())
                {
                    var faceServiceClient = new FaceServiceClient(FaceApiKey);
                    recognizedFaces = await faceServiceClient.DetectAsync(stream, false, true, true, false);
                }
                try
                {
                    await tempImageFile.DeleteAsync();
                }
                catch { }

                // Display recognition results
                //await UpdateStatusAsync("Displaying Microsoft 'Project Oxford' Face API results...");
                StringBuilder stringBuilder = new StringBuilder();
                if (recognizedFaces.Count() == 0)
                {
                    stringBuilder.AppendLine("No faces found");
                }
                else
                {
                    foreach (var face in recognizedFaces)
                    {
                        stringBuilder.AppendLine($"Gender: {face.Attributes.Gender} Age: {face.Attributes.Age}");
                    }
                }
                await UpdateStatusAsync(stringBuilder.ToString());

                // Wait a few seconds seconds to give viewers a chance to appreciate all we've done
                CountdownStoryboard.Begin();
                await Task.Delay(ControlLoopDelayMilliseconds);
            }
        }

        private async Task UpdateStatusAsync(string message)
        {
            await Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal,
                () =>
                {
                    StatusTextBlock.Text = message;
                });
        }
    }
}