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
        private const string FaceApiKey = "c43de4470d9c41dabc5292d3356098ec";
        private const int ControlLoopDelayMilliseconds = 5000; // Update the CountdownStoryboard as well!
        private static readonly FaceServiceClient faceServiceClient = new FaceServiceClient(FaceApiKey);

        private MediaCapture mediaCapture;
        private Random random = new Random();

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
            await UpdateStatusAsync("Előnézeti kép előkészítése...");

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
                await UpdateStatusAsync("Fotózás...");

                // TODO focus if possible
                //await mediaCapture.VideoDeviceController.FocusControl.FocusAsync();
                FaceResultsGrid.Children.Clear();
                CountdownProgressBar.Value = 100;
                CameraFlashStoryboard.Begin();

                using (var stream = new InMemoryRandomAccessStream())
                {
                    var imageEncodingProperties = ImageEncodingProperties.CreatePng();
                    imageEncodingProperties.Width = 320;
                    imageEncodingProperties.Height = 200;
                    await mediaCapture.CapturePhotoToStreamAsync(imageEncodingProperties, stream);

                    // Display camera picture
                    await UpdateStatusAsync("Minta megjelenítése...");

                    stream.Seek(0);
                    var bitmapImage = new BitmapImage();
                    await bitmapImage.SetSourceAsync(stream);
                    ResultImage.Source = bitmapImage;

                    // Send picture for recognition
                    // We need to encode the raw image as a JPEG to make sure the service can recognize it.
                    await UpdateStatusAsync("Kép elküldése a Microsoft Cognitive Services Face API-nak...");
                    stream.Seek(0);

                    var recognizedFaces = await GetFaces(stream.AsStreamForRead());
                    var recommendations = GetRecommendations(recognizedFaces);
                    var adults = recognizedFaces.Count(f => f.FaceAttributes.Age >= 18);
                    var children = recognizedFaces.Count(f => f.FaceAttributes.Age < 18);

                    // Display recognition results
                    // Wait a few seconds seconds to give viewers a chance to appreciate all we've done
                    await UpdateStatusAsync($"{adults} felnőtt, {children} gyerek. A tájékoztatás nem minősül ajánlattételnek, részletek a bankfiókban vagy a kh.hu-n.");

                    // The face rectangles received from Face API are measured in pixels of the raw image.
                    // We need to calculate the extra scaling and displacement that results from the raw image
                    // being displayed in a larger container.
                    // We use the FaceResultsGrid as a basis for the calculation, because the ResultImage control's ActualHeight and ActualWidth
                    // properties have the same aspect ratio as the image, and not the aspect ratio of the screen.
                    double widthScaleFactor = FaceResultsGrid.ActualWidth / bitmapImage.PixelWidth;
                    double heightScaleFactor = FaceResultsGrid.ActualHeight / bitmapImage.PixelHeight;
                    double scaleFactor = Math.Min(widthScaleFactor, heightScaleFactor);

                    bool isTheBlackSpaceOnTheLeft = widthScaleFactor > heightScaleFactor;
                    double extraLeftNeeded = 0;
                    double extraTopNeeded = 0;
                    if (isTheBlackSpaceOnTheLeft) extraLeftNeeded = (FaceResultsGrid.ActualWidth - scaleFactor * bitmapImage.PixelWidth) / 2;
                    else extraTopNeeded = (FaceResultsGrid.ActualHeight - scaleFactor * bitmapImage.PixelHeight) / 2;

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
                        faceInfoTextBlock.Text = $"{GetGenderString(face.FaceAttributes.Gender)}, {face.FaceAttributes.Age} YEARS OLD, {Math.Round(face.FaceAttributes.Smile * 100, 0)}% SMILING";
                        Border faceInfoBorder = new Border();
                        faceInfoBorder.Background = new SolidColorBrush(Colors.Black);
                        faceInfoBorder.Padding = new Thickness(5);
                        faceInfoBorder.Child = faceInfoTextBlock;
                        faceInfoBorder.HorizontalAlignment = HorizontalAlignment.Left;
                        faceInfoBorder.VerticalAlignment = VerticalAlignment.Top;
                        faceInfoBorder.Margin = new Thickness(faceOutlineRectangleLeft, faceOutlineRectangleTop - 50, 0, 0);
                        FaceResultsGrid.Children.Add(faceInfoBorder);

                        string recommendation = "";
                        if (recommendations.ContainsKey(face.FaceId)) recommendation = recommendations[face.FaceId];

                        TextBlock recommendationInfoTextBlock = new TextBlock();
                        recommendationInfoTextBlock.Foreground = new SolidColorBrush(Colors.White);
                        recommendationInfoTextBlock.FontSize = 30;
                        recommendationInfoTextBlock.Text = recommendation;
                        Border recommendationInfoBorder = new Border();
                        recommendationInfoBorder.Background = new SolidColorBrush(Colors.Black);
                        recommendationInfoBorder.Padding = new Thickness(5);
                        recommendationInfoBorder.Child = recommendationInfoTextBlock;
                        recommendationInfoBorder.HorizontalAlignment = HorizontalAlignment.Left;
                        recommendationInfoBorder.VerticalAlignment = VerticalAlignment.Top;
                        recommendationInfoBorder.Margin = new Thickness(faceOutlineRectangleLeft, faceOutlineRectangleTop + faceOutlineRectangleHeight, 0, 0);
                        FaceResultsGrid.Children.Add(recommendationInfoBorder);
                    }
                }

                CountdownStoryboard.Begin();
                await Task.Delay(ControlLoopDelayMilliseconds);
            }
        }

        private static async Task<Face[]> GetFaces(Stream stream)
        {

            var result = await faceServiceClient.DetectAsync(stream, returnFaceAttributes: new List<FaceAttributeType> { FaceAttributeType.Age, FaceAttributeType.Gender, FaceAttributeType.Smile });
            return result;
        }

        private string GetGenderString(string originalValue)
        {
            if (originalValue == "male")
                return "GENTLEMAN";
            else
                return "LADY";
        }

        /// <summary>
        /// Gets recommendations for a group of faces and returns them indexed by Face ID.
        /// </summary>
        private Dictionary<Guid, string> GetRecommendations(Face[] faces)
        {
            // Create recommendations dictionary
            var recommendations = new Dictionary<Guid, string>();

            // First, let's see if this is a recognized group. 
            // If it is, assign a group recommendation to everyone.
            int adults = faces.Count(f => f.FaceAttributes.Age >= 18);
            int children = faces.Count(f => f.FaceAttributes.Age < 18);
            string groupRecommendation = null;

            #region Group recommendations
            if (adults == 1 && children == 1)
            {
                groupRecommendation = GetRandomRecommendationFromList
                    (new List<string>
                    {
                        "Lakáscélú hitel állam támogatással",
                        "K&H trambulin bankszámla és K&H lakásbiztosítás"
                    });
            }
            else if (adults == 1 && children == 2)
            {
                groupRecommendation = GetRandomRecommendationFromList
                    (new List<string>
                    {
                        "Családok Otthonteremtési kedvezménye (CSOK)",
                        "K&H tervező megtakarítási számla"
                    });
            }
            else if (adults == 1 && children == 3)
            {
                groupRecommendation = GetRandomRecommendationFromList
                    (new List<string>
                    {
                        "Családok Otthonteremtési kedvezménye (CSOK)",
                        "K&H trambulin megtakarítási betétszámla"
                    });
            }
            else if (adults == 2 && children == 1)
            {
                groupRecommendation = GetRandomRecommendationFromList
                    (new List<string>
                    {
                        "Lakáscélú hitel állam támogatással",
                        "K&H trambulin bankszámla és K&H lakásbiztosítás"
                    });
            }
            else if (adults == 2 && children == 2)
            {
                groupRecommendation = GetRandomRecommendationFromList
                    (new List<string>
                    {
                        "Családok Otthonteremtési kedvezménye (CSOK)",
                        "K&H tervező megtakarítási számla és K&H lakásbiztosítás"
                    });
            }
            else if (adults == 2 && children == 3)
            {
                groupRecommendation = GetRandomRecommendationFromList
                    (new List<string>
                    {
                        "Családok Otthonteremtési kedvezménye (CSOK)",
                        "K&H hozamhalmozó életbiztosítás és K&H trambulin megtakarítási betétszámla"
                    });
            }
            #endregion

            // This is a recognized group. Give everyone the same recommendation.
            if (groupRecommendation != null)
            {
                foreach (var face in faces) recommendations.Add(face.FaceId, groupRecommendation);
            }
            // This is not a recognized group. Give individualized recommendations.
            else
            {
                foreach (var face in faces)
                {
                    string individualRecommendation = "";
                    #region Male recommendations
                    if (face.FaceAttributes.Gender == "male")
                    {
                        if (face.FaceAttributes.Age <= 18)
                        {
                            individualRecommendation = GetRandomRecommendationFromList(new List<string>
                            {
                                "K&H trambulin megtakarítási betétszámla",
                                "K&H trambulin bankszámla"
                            });
                        }
                        else if (face.FaceAttributes.Age <= 25)
                        {
                            individualRecommendation = GetRandomRecommendationFromList(new List<string>
                            {
                                "K&H tervező megtakarítási számla",
                                "Lakáshitel",
                                "K&H mobilbank",
                                "K&H lakásbiztosítás"
                            });
                        }
                        else if (face.FaceAttributes.Age <= 35)
                        {
                            individualRecommendation = GetRandomRecommendationFromList(new List<string>
                            {

                                "Lakáshitel",
                                "K&H nyugdíjelőtakarékossági számla",
                                "K&H bővített plusz számlacsomag",
                                "Családok Otthonteremtési kedvezménye (CSOK)"
                            });
                        }
                        else if (face.FaceAttributes.Age <= 40)
                        {
                            individualRecommendation = GetRandomRecommendationFromList(new List<string>
                            {
                                "K&H rendszeres díjas nyugdíjbiztosítás",
                                "K&H World Mastercard plusz hitelkártya",
                                "K&H lakásbiztosítás",
                                "K&H hozamhalmozó életbiztosítás"
                            });
                        }
                        else
                        {
                            individualRecommendation = GetRandomRecommendationFromList(new List<string>
                            {
                                "K&H hozamhalmozó életbiztosítás",
                                "K&H bővített plusz számlacsomag",
                                "K&H lakásbiztosítás",
                                "K&H tervező megtakarítási számla"
                            });
                        }
                    }
                    #endregion
                    #region Female recommendations
                    else
                    {
                        if (face.FaceAttributes.Age <= 18)
                        {
                            individualRecommendation = GetRandomRecommendationFromList(new List<string>
                            {
                                "K&H trambulin megtakarítási betétszámla",
                                "K&H trambulin bankszámla"
                            });
                        }
                        else if (face.FaceAttributes.Age <= 25)
                        {
                            individualRecommendation = GetRandomRecommendationFromList(new List<string>
                            {
                                "K&H tervező megtakarítási számla",
                                "K&H biztostárs utasbiztosítás",
                                "K&H minimum plusz számlacsomag",
                                "K&H lakásbiztosítás"
                            });
                        }
                        else if (face.FaceAttributes.Age <= 35)
                        {
                            individualRecommendation = GetRandomRecommendationFromList(new List<string>
                            {
                                "Hitelkártya",
                                "Lakáshitel",
                                "K&H lakásbiztosítás",
                                "K&H bővített plusz számlacsomag"
                            });
                        }
                        else if (face.FaceAttributes.Age <= 40)
                        {
                            individualRecommendation = GetRandomRecommendationFromList(new List<string>
                            {
                                "K&H kényelmi plusz számlacsomag",
                                "K&H nyugdíjelőtakarékossági számla",
                                "K&H lakásbiztosítás"
                            });
                        }
                        else
                        {
                            individualRecommendation = GetRandomRecommendationFromList(new List<string>
                            {
                                "K&H lakásbiztosítás",
                                "K&H World Mastercard plusz hitelkártya",
                                "K&H tervező megtakarítási számla"
                            });
                        }
                    }
                    #endregion
                    recommendations.Add(face.FaceId, individualRecommendation);
                }
            }

            return recommendations;
        }

        private string GetRandomRecommendationFromList(List<string> recommendations)
        {
            if (recommendations.Count > 0)
            {
                return recommendations[random.Next(0, recommendations.Count)];
            }
            else
            {
                return "Nincs ajánlás";
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
