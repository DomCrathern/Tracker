using Android.App;
using Android.OS;
using Android.Support.V7.App;
using Android.Views;
using Android.Widget;
using Plugin.CurrentActivity;
using Plugin.Geolocator;
using Plugin.Geolocator.Abstractions;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Xml;

namespace Tracker
{
    [Activity(Label = "@string/app_name", Theme = "@style/AppTheme", MainLauncher = true)]
    public class MainActivity : AppCompatActivity
    {
        private Position currentPosition;
        private List<Position> route = new List<Position>();
        private GpsService gpsService;

        protected override void OnCreate(Bundle savedInstanceState)
        {
            base.OnCreate(savedInstanceState);

            SetContentView(Resource.Layout.activity_main);

            CrossCurrentActivity.Current.Init(this, savedInstanceState);
            var geolocator = CrossGeolocator.Current;
            geolocator.DesiredAccuracy = 100;

            TextView infoTextView = FindViewById<TextView>(Resource.Id.infoTextView);
            if (!geolocator.IsGeolocationAvailable)
            {
                infoTextView.Text = "GPS is unavailable";
            }
            if (!geolocator.IsGeolocationEnabled)
            {
                infoTextView.Text = "GPS is disabled";
            }
            else
            {
                infoTextView.Text = "Ready to track...";

                Button rightButton = FindViewById<Button>(Resource.Id.finishButton);
                Button leftButton = FindViewById<Button>(Resource.Id.startPauseButton);
                leftButton.Enabled = true;

                leftButton.Click += async (sender, e) =>
                {
                    try
                    {
                        if (leftButton.Text == "Start" || leftButton.Text == "Resume")
                        {
                            leftButton.Enabled = false;
                            rightButton.Visibility = ViewStates.Invisible;

                            await StartTracking();

                            leftButton.Text = "Pause";
                            infoTextView.Text = "Tracking...";
                            leftButton.Enabled = true;
                        }
                        else if (leftButton.Text == "Pause")
                        {
                            await StopTracking();

                            infoTextView.Text = "Tracking paused...";
                            leftButton.Text = "Resume";
                            rightButton.Visibility = ViewStates.Visible;
                        }
                        else // Discard
                        {
                            
                        }
                    }
                    catch (TaskCanceledException)
                    {
                        infoTextView.Text = "GPS timed out";
                    }
                    catch (Exception ex)
                    {
                        infoTextView.Text = ex.ToString();
                    }
                };

                rightButton.Click += async (sender, e) =>
                {
                    try
                    {
                        if (rightButton.Text == "Finish")
                        {
                            await StopTracking();

                            leftButton.Text = "Discard";
                            rightButton.Text = "Save";
                        }
                        else // Save
                        {
                            WriteGpxFile();

                            route.Clear();
                        }
                    }
                    catch (Exception ex)
                    {
                        infoTextView.Text = ex.ToString();
                    }
                };
            }
        }

        protected override void OnPause()
        {
            base.OnPause();
        }

        protected override void OnResume()
        {
            base.OnResume();
        }

        private void LogPosition(Position position)
        {
            var newPositionDetails = $"lat={position.Latitude} lon={position.Longitude} ele={position.Altitude} time={position.Timestamp.UtcDateTime.ToString("s")}Z";
            TextView locationTextView = FindViewById<TextView>(Resource.Id.locationTextView);
            locationTextView.Text += newPositionDetails + "\r\n";

            route.Add(position);
        }

        private void WriteGpxFile()
        {
            string folderPath = System.Environment.GetFolderPath(System.Environment.SpecialFolder.ApplicationData);

            string filePath = "/storage/sdcard0/android/data/tracker.tracker/files/tracks/" + "bike" + Regex.Replace(DateTime.UtcNow.ToString("o") + ".gpx", @"[/:\s]", "");
            FileInfo fileInfo = new FileInfo(filePath);

            fileInfo.Directory.Create();

            XmlWriterSettings xmlWriterSettings = new XmlWriterSettings { Indent = true };
            using (XmlWriter xmlWriter = XmlWriter.Create(filePath, xmlWriterSettings))
            {
                xmlWriter.WriteStartDocument();

                xmlWriter.WriteStartElement("gpx", "http://www.topografix.com/GPX/1/1");
                xmlWriter.WriteAttributeString("creator", "Tracker");
                xmlWriter.WriteAttributeString("xmlns", "xsi", null, "http://www.w3.org/2001/XMLSchema-instance");
                xmlWriter.WriteAttributeString("xsi", "schemaLocation", null, "http://www.topografix.com/GPX/1/1 http://www.topografix.com/GPX/1/1/gpx.xsd");
                xmlWriter.WriteAttributeString("version", "1.1");

                xmlWriter.WriteStartElement("metadata");
                xmlWriter.WriteElementString("time", DateTime.UtcNow.ToString("o"));
                xmlWriter.WriteEndElement();

                xmlWriter.WriteStartElement("trk");

                xmlWriter.WriteStartElement("trkseg");

                foreach (var position in route)
                {
                    xmlWriter.WriteStartElement("trkpt");
                    xmlWriter.WriteAttributeString("lat", position.Latitude.ToString());
                    xmlWriter.WriteAttributeString("lon", position.Longitude.ToString());

                    xmlWriter.WriteElementString("ele", position.Altitude.ToString());
                    xmlWriter.WriteElementString("time", position.Timestamp.ToString("o"));

                    xmlWriter.WriteEndElement();
                }

                xmlWriter.WriteEndElement(); // trkseg

                xmlWriter.WriteEndElement(); // trk

                xmlWriter.WriteEndElement(); // gpx

                xmlWriter.WriteEndDocument();
            }
        }

        private async Task StartTracking()
        {
            var geolocator = CrossGeolocator.Current;
            if (geolocator.IsListening)
            {
                return;
            }

            await geolocator.StartListeningAsync(TimeSpan.FromSeconds(5), 10, true);

            geolocator.PositionChanged += PositionChanged;
            geolocator.PositionError += PositionError;
        }

        private async Task StopTracking()
        {
            var geolocator = CrossGeolocator.Current;
            if (!geolocator.IsListening)
            {
                return;
            }

            await geolocator.StopListeningAsync();

            geolocator.PositionChanged -= PositionChanged;
            geolocator.PositionError -= PositionError;
        }

        private void PositionChanged(object sender, PositionEventArgs e)
        {
            var newPosition = e.Position;

            // PositionChanged seems to be fired twice for every position change, so only log the first
            if (currentPosition != null && e.Position.Timestamp == currentPosition.Timestamp)
            {
                return;
            }

            LogPosition(newPosition);

            currentPosition = newPosition;
        }

        private async void PositionError(object sender, PositionErrorEventArgs e)
        {
            TextView infoTextView = FindViewById<TextView>(Resource.Id.infoTextView);
            infoTextView.Text = "Position error occurred, restarting tracking...";

            // Restart tracking after error
            await StopTracking();
            await StartTracking();
        }
    }
}