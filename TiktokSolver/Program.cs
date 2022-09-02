using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using Emgu.CV;
using Emgu.CV.CvEnum;
using Emgu.CV.Structure;
using Emgu.CV.Util;
using OpenQA.Selenium;
using OpenQA.Selenium.Appium;
using OpenQA.Selenium.Appium.Android;
using OpenQA.Selenium.Appium.MultiTouch;

namespace TiktokSolver
{
    class Program
    {
        //secsdk-captcha-drag-wrapper
        private const string sliderElement = "/hierarchy/android.widget.FrameLayout/android.widget.FrameLayout/android.widget.FrameLayout/android.widget.FrameLayout/android.widget.FrameLayout/android.webkit.WebView/android.webkit.WebView/android.view.View/android.app.Dialog/android.view.View[3]/android.view.View[2]";
        //captcha-verify-image
        private const string puzzleElement = "/hierarchy/android.widget.FrameLayout/android.widget.FrameLayout/android.widget.FrameLayout/android.widget.FrameLayout/android.widget.FrameLayout/android.webkit.WebView/android.webkit.WebView/android.view.View/android.app.Dialog/android.view.View[2]/android.widget.Image";
        //screen resulotion distance
        private const int pointFactor = 14;

        static AndroidElement WaitElement(AndroidDriver<AndroidElement> driver, string xPath)
        {
            var tst = driver.FindElements(By.XPath(xPath));

            while (tst.Count == 0)
            {
                Thread.Sleep(TimeSpan.FromSeconds(1));
                tst = driver.FindElements(By.XPath(xPath));
            }

            return tst[0];
        }

        static Image<Gray, Byte> ProcessPuzzleImage(Image<Bgr, Byte> srcImage)
        {
            var grayImage = srcImage.Convert<Gray, Byte>();
            CvInvoke.GaussianBlur(grayImage, grayImage, new Size(3, 3), 0);
            CvInvoke.Threshold(grayImage, grayImage, 128, 255, ThresholdType.Binary);

            return grayImage;
        }

        static Image<Gray, Byte> ProcessDiffImage(Image<Bgr, Byte> srcImage)
        {
            var kernel = Mat.Ones(5, 5, DepthType.Cv8U,1);
            var anchor = new Point(-1, -1);

            var grayImage = srcImage.Convert<Gray, Byte>();

            CvInvoke.GaussianBlur(grayImage, grayImage, new Size(3, 3), 0);
            CvInvoke.Canny(grayImage, grayImage, 100, 200);
            
            for (var i = 0; i < 10; i++)
            {
                CvInvoke.Dilate(grayImage, grayImage, kernel, anchor, 1, BorderType.Default, new MCvScalar());
                CvInvoke.Erode(grayImage, grayImage, kernel, anchor, 1, BorderType.Default, new MCvScalar());
            }
            
            CvInvoke.Threshold(grayImage, grayImage, 0, 255, ThresholdType.Binary);

            return grayImage;
        }

        static Point ProcessPosition(Image<Bgr, Byte> puzzleImage, Image<Bgr, Byte> diffImage)
        {
            var puzzle = ProcessPuzzleImage(puzzleImage);
            var diff = ProcessDiffImage(diffImage);

            var contours1 = new VectorOfVectorOfPoint();
            var contours2 = new VectorOfVectorOfPoint();

            CvInvoke.FindContours(puzzle, contours1, null, RetrType.Tree, ChainApproxMethod.ChainApproxSimple);
            CvInvoke.FindContours(diff, contours2, null, RetrType.Tree, ChainApproxMethod.ChainApproxSimple);

            var listRectangle1 = new List<Rectangle>();
            var listRectangle2 = new List<Rectangle>();

            for (var i = 0; i < contours1.Size; i++)
                listRectangle1.Add(CvInvoke.BoundingRectangle(contours1[i]));

            for (var i = 0; i < contours2.Size; i++)
                listRectangle2.Add(CvInvoke.BoundingRectangle(contours2[i]));

            var orderRectangle = listRectangle2.OrderByDescending(x => x.Width * x.Height).First();
            var found = listRectangle1.OrderBy(x => Math.Abs((orderRectangle.Width * orderRectangle.Height) - (x.Width * x.Height))).First();

            return new Point(found.X, found.Y);
        }

        static Image<Bgr, Byte> CreateImageFromBytesArray(byte[] bytes)
        {
            using (var ms = new MemoryStream(bytes))
                return ((Bitmap)Image.FromStream(ms)).ToImage<Bgr, byte>();
        }

        static void Main(string[] args)
        {
            var appiumOptions = new AppiumOptions();
            appiumOptions.AddAdditionalCapability("platformName", "android");
            appiumOptions.AddAdditionalCapability("appPackage", "com.zhiliaoapp.musically");
            appiumOptions.AddAdditionalCapability("appActivity", "com.ss.android.ugc.aweme.splash.SplashActivity");

            var driver = new AndroidDriver<AndroidElement>(new Uri("http://127.0.0.1:4723/wd/hub"), appiumOptions);
            var found = false;

            Console.WriteLine("Solving...");

            while (!found)
            {
                var action = new TouchAction(driver);
                
                var puzzleImageElement = WaitElement(driver, puzzleElement);
                var puzzleSliderElement = WaitElement(driver, sliderElement);

                while (!WaitElement(driver, puzzleElement).Enabled || !WaitElement(driver, sliderElement).Enabled)
                {
                    Thread.Sleep(TimeSpan.FromMilliseconds(500));
                    puzzleImageElement = WaitElement(driver, puzzleElement);
                    puzzleSliderElement = WaitElement(driver, sliderElement);
                }

                var rectSlider = puzzleSliderElement.Rect;
                var locationSlider = puzzleSliderElement.Location;

                var xPosition = locationSlider.X + (rectSlider.Width / 2) + rectSlider.Width;
                var yPosition = locationSlider.Y + (rectSlider.Height / 2);

                var imgFirst = CreateImageFromBytesArray(puzzleImageElement.GetScreenshot().AsByteArray);

                action.Press(xPosition - rectSlider.Width, yPosition)
                    .MoveTo(xPosition, yPosition)
                    .Perform();

                var imgLast = CreateImageFromBytesArray(puzzleImageElement.GetScreenshot().AsByteArray);

                var diff = imgLast.AbsDiff(imgFirst);

                var diffCrop = diff.Copy(new Rectangle(0, 0, rectSlider.Width + 5, diff.Size.Height));
                var imgFirstCrop = imgFirst.Copy(new Rectangle(rectSlider.Width, 0, imgFirst.Width - rectSlider.Width, imgFirst.Height));

                var detectInfo = ProcessPosition(imgFirstCrop, diffCrop);

                var currentPos = xPosition;
                var nextPos = (currentPos + detectInfo.X) - pointFactor;

                var currStep = 0;
                var endStep = nextPos - currentPos;

                var percent = endStep / 100.0;

                while (currStep < 100)
                {
                    currStep += 25;
                    action.MoveTo(xPosition + (percent * currStep), yPosition);
                }

                action.Release();
                action.Perform();

                found = !WaitElement(driver, puzzleElement).Displayed;
                Thread.Sleep(TimeSpan.FromSeconds(1));
            }

            Console.WriteLine("Solved");
            Console.Read();
        }
    }
}
