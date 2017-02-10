////////////////////////////////////////////////////////////////////////////
//
// Copyright 2016 Realm Inc.
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
// http://www.apache.org/licenses/LICENSE-2.0
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.
//
////////////////////////////////////////////////////////////////////////////

using System;
using System.Diagnostics;
using System.Threading;
using DrawXShared;
using Foundation;
using Realms.Sync;
using SkiaSharp.Views.iOS;
using UIKit;

namespace DrawX.IOS
{
    // Most of the ViewController logic is factored out here so we can subclass
    // with a local copy in case we want a debug build which bypasses nuget as a Realm source
    public class ViewControllerShared : UIViewController
    {
        private RealmDraw _drawer;
        private bool _hasShownCredentials;  // flag to show on initial layout only
        private CoreGraphics.CGRect _prevBounds;
        private float _devicePixelMul;  // usually 2.0 except on weird iPhone 6+

        public ViewControllerShared(IntPtr handle) : base(handle)
        {
            _devicePixelMul = (float)UIScreen.MainScreen.Scale;
        }

        public override void ViewDidLoad()
        {
            base.ViewDidLoad();

            DrawXSettingsManager.InitLocalSettings();
            var user = User.Current;
            if (user != null)
            {
                SetupDrawer();
                _drawer.CreateSynchronizedRealm(user);
            }
        }

        private void SetupDrawer()
        {
            // scale bounds to match the pixel dimensions of the SkiaSurface
            _drawer = new RealmDraw(
                _devicePixelMul * (float)View.Bounds.Width,
                _devicePixelMul * (float)View.Bounds.Height);
            _prevBounds = View.Bounds;
            _drawer.CredentialsEditor = () =>
            {
                InvokeOnMainThread(EditCredentials);
            };

            _drawer.RefreshOnRealmUpdate = () =>
            {
                View?.SetNeedsDisplay();  // just refresh on notification, OnPaintSample below triggers DrawTouches
            };

            _drawer.ReportError = (bool isError, string msg) =>
            {
                var alertController = UIAlertController.Create(isError?"Realm Error":"Warning", msg, UIAlertControllerStyle.Alert);
                alertController.AddAction(UIAlertAction.Create("Ok", UIAlertActionStyle.Default, null));
                PresentViewController(alertController, true, null);
            };
        }

        public override void ViewDidLayoutSubviews()
        {
            base.ViewDidLayoutSubviews();

            // this is the earliest we can show the modal login
            // show unconditionally on launch
            if (_drawer?.Realm != null || _hasShownCredentials)
            {
                if (View.Bounds != _prevBounds)
                {
                    SetupDrawer();
                    var user = DrawXSettingsManager.LoggedInUser;
                    if (user != null)
                    {
                        _drawer.LoginToServerAsync(user);
                        _hasShownCredentials = true;  // skip credentials if saved user in store
                    }
                    View?.SetNeedsDisplay();
                }
            }
            else
            {
                EditCredentials();
                _hasShownCredentials = true;
            }
        }

        protected void OnPaintSample(object sender, SKPaintSurfaceEventArgs e)
        {
            _drawer?.DrawTouches(e.Surface.Canvas);
        }

        public override void TouchesBegan(NSSet touches, UIEvent evt)
        {
            base.TouchesBegan(touches, evt);
            var touch = touches.AnyObject as UITouch;
            if (touch != null)
            {
                var point = touch.LocationInView(View);
                _drawer?.StartDrawing((float)point.X * _devicePixelMul, (float)point.Y * _devicePixelMul);
                View.SetNeedsDisplay();  // probably after touching Pencils
            }
        }

        public override void TouchesMoved(NSSet touches, UIEvent evt)
        {
            base.TouchesMoved(touches, evt);
            var touch = touches.AnyObject as UITouch;
            if (touch != null)
            {
                var point = touch.LocationInView(View);
                _drawer?.AddPoint((float)point.X * _devicePixelMul, (float)point.Y * _devicePixelMul);
                View.SetNeedsDisplay();
            }
        }

        public override void TouchesCancelled(NSSet touches, UIEvent evt)
        {
            base.TouchesCancelled(touches, evt);
            var touch = touches.AnyObject as UITouch;
            if (touch != null)
            {
                _drawer?.CancelDrawing();
            }
        }

        public override void TouchesEnded(NSSet touches, UIEvent evt)
        {
            base.TouchesEnded(touches, evt);
            var touch = touches.AnyObject as UITouch;
            if (touch != null)
            {
                var point = touch.LocationInView(View);
                _drawer?.StopDrawing((float)point.X * _devicePixelMul, (float)point.Y * _devicePixelMul);
            }

            View.SetNeedsDisplay();
        }

        public override void MotionBegan(UIEventSubtype motion, UIEvent evt)
        {
            if (motion == UIEventSubtype.MotionShake)
            {
                var alert = UIAlertController.Create(
                    "Erase Canvas?",
                    "This will clear the shared Realm database and erase the canvas. Are you sure you wish to proceed?",
                    UIAlertControllerStyle.Alert);
                alert.AddAction(UIAlertAction.Create("Erase", UIAlertActionStyle.Destructive, action => _drawer?.ErasePaths()));
                alert.AddAction(UIAlertAction.Create("Cancel", UIAlertActionStyle.Cancel, null));
                if (alert.PopoverPresentationController != null)
                {
                    alert.PopoverPresentationController.SourceView = View;
                }
                PresentViewController(alert, animated: true, completionHandler: null);
                //// unlike other gesture actions, don't call View.SetNeedsDisplay but let major Realm change prompt redisplay
            }
        }

        // invoked as callback from pressing a control area in drawing surface, or at startup
        private void EditCredentials()
        {
            var sb = UIStoryboard.FromName("LoginScreen", null);
            var loginVC = sb.InstantiateViewController("Login") as LoginViewController;
            loginVC.PerformLoginAsync = async (credentials) =>
            {
                if (credentials != null)
                {
                    var user = await User.LoginAsync(credentials, new Uri($"http://{DrawXSettingsManager.Settings.ServerIP}"));
                    SetupDrawer();
                    _drawer.CreateSynchronizedRealm(user);
                }

                loginVC.DismissViewController(true, null);
                View.SetNeedsDisplay();
            };

            PresentViewController(loginVC, false, null);
        }
    }
}
