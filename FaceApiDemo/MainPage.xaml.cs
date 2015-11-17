using Microsoft.ProjectOxford.Face;
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
using Windows.UI;
using Windows.UI.Popups;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;
using Windows.UI.Xaml.Data;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Media.Imaging;
using Windows.UI.Xaml.Navigation;
using Windows.UI.Xaml.Shapes;

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
            Window.Current.SizeChanged += Current_SizeChanged;

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

        protected override void OnNavigatedFrom(NavigationEventArgs e)
        {
            base.OnNavigatedFrom(e);
            Window.Current.SizeChanged -= Current_SizeChanged;
        }

        private void Current_SizeChanged(object sender, Windows.UI.Core.WindowSizeChangedEventArgs e)
        {
            // If the window is resized, delete any face rectangles.
            // TODO Recalculate face rectangle positions instead of just deleting them.
            FaceResultsGrid.Children.Clear();
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
                FaceResultsGrid.Children.Clear();
                CountdownProgressBar.Value = 100;
                CameraFlashStoryboard.Begin();

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
                await UpdateStatusAsync("Uploading picture to Microsoft Project Oxford Face API...");
                var recognizedFaces = await GetFaces(softwareBitmap);

                // Display recognition results
                // Wait a few seconds seconds to give viewers a chance to appreciate all we've done
                await UpdateStatusAsync($"{recognizedFaces.Count()} face(s) found by Microsoft 'Project Oxford' Face API");

                // The face rectangles received from Face API are measured in pixels of the raw image.
                // We need to calculate the extra scaling and displacement that results from the raw image
                // being displayed in a larger container.
                // We use the FaceResultsGrid as a basis for the calculation, because the ResultImage control's ActualHeight and ActualWidth
                // properties have the same aspect ratio as the image, and not the aspect ratio of the screen.
                double widthScaleFactor = FaceResultsGrid.ActualWidth / softwareBitmap.PixelWidth;
                double heightScaleFactor = FaceResultsGrid.ActualHeight / softwareBitmap.PixelHeight;
                double scaleFactor = Math.Min(widthScaleFactor, heightScaleFactor);

                bool isTheBlackSpaceOnTheLeft = widthScaleFactor > heightScaleFactor;
                double extraLeftNeeded = 0;
                double extraTopNeeded = 0;
                if (isTheBlackSpaceOnTheLeft) extraLeftNeeded = (FaceResultsGrid.ActualWidth - scaleFactor * softwareBitmap.PixelWidth) / 2;
                else extraTopNeeded = (FaceResultsGrid.ActualHeight - scaleFactor * softwareBitmap.PixelHeight) / 2;

                foreach (var face in recognizedFaces)
                {
                    var faceOutlineRectangleLeft = extraLeftNeeded + scaleFactor * face.FaceRectangle.Left;
                    var faceOutlineRectangleTop = extraTopNeeded + scaleFactor * face.FaceRectangle.Top;
                    var faceOutlineRectangleHeight = scaleFactor * face.FaceRectangle.Height;
                    var faceOutlineRectangleWidth = scaleFactor * face.FaceRectangle.Width;

                    Rectangle faceOutlineRectangle = new Rectangle();
                    faceOutlineRectangle.Stroke = new SolidColorBrush(Colors.Black);
                    faceOutlineRectangle.StrokeThickness = 3;
                    faceOutlineRectangle.HorizontalAlignment = HorizontalAlignment.Left;
                    faceOutlineRectangle.VerticalAlignment = VerticalAlignment.Top;
                    faceOutlineRectangle.Margin = new Thickness(faceOutlineRectangleLeft, faceOutlineRectangleTop, 0, 0);
                    faceOutlineRectangle.Height = faceOutlineRectangleHeight;
                    faceOutlineRectangle.Width = faceOutlineRectangleWidth;
                    FaceResultsGrid.Children.Add(faceOutlineRectangle);

                    TextBlock faceInfoTextBlock = new TextBlock();
                    faceInfoTextBlock.Foreground = new SolidColorBrush(Colors.White);
                    faceInfoTextBlock.FontSize = 30;
                    faceInfoTextBlock.Text = $"{face.Attributes.Gender}, {face.Attributes.Age}";
                    Border faceInfoBorder = new Border();
                    faceInfoBorder.Background = new SolidColorBrush(Colors.Black);
                    faceInfoTextBlock.Padding = new Thickness(5);
                    faceInfoBorder.Child = faceInfoTextBlock;
                    faceInfoBorder.HorizontalAlignment = HorizontalAlignment.Left;
                    faceInfoBorder.VerticalAlignment = VerticalAlignment.Top;
                    faceInfoBorder.Margin = new Thickness(faceOutlineRectangleLeft, faceOutlineRectangleTop - 50, 0, 0);
                    FaceResultsGrid.Children.Add(faceInfoBorder);
                }

                CountdownStoryboard.Begin();
                await Task.Delay(ControlLoopDelayMilliseconds);
            }
        }

        private static async Task<Face[]> GetFaces(SoftwareBitmap softwareBitmap)
        {
            using (var ms = new InMemoryRandomAccessStream())
            {
                var bitmapEncoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, ms);
                bitmapEncoder.SetSoftwareBitmap(softwareBitmap);
                await bitmapEncoder.FlushAsync();
                var faceServiceClient = new FaceServiceClient(FaceApiKey);
                ms.Seek(0);
                var result = await faceServiceClient.DetectAsync(ms.AsStreamForRead(), false, true, true, false);
                return result;
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
