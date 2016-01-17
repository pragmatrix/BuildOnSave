using System;
using System.Drawing;
using System.Windows.Forms;
using EnvDTE;
using Microsoft.VisualStudio.CommandBars;
using stdole;

namespace BuildOnSave
{

	sealed class DriverUI : IDisposable
	{
		readonly DTE _dte;
		readonly Window _outputWindow;
		readonly OutputWindowPane _pane;
		readonly CommandBarButton _barButton_;
		readonly StdPicture[] _statusImages =
		{
			imageToPicture(ImageBuilder.createStatusImage(VSColors.Notification.Warning, false)),
			imageToPicture(ImageBuilder.createStatusImage(VSColors.Notification.Positive, false)),
			imageToPicture(ImageBuilder.createStatusImage(VSColors.Notification.Negative, false))
		};
		readonly StdPicture[] _statusImagesProcessing =
		{
			imageToPicture(ImageBuilder.createStatusImage(VSColors.Notification.Warning, true)),
			imageToPicture(ImageBuilder.createStatusImage(VSColors.Notification.Positive, true)),
			imageToPicture(ImageBuilder.createStatusImage(VSColors.Notification.Negative, true))
		};

		BuildStatus _status = BuildStatus.Indeterminate;
		bool _processing;

		public DriverUI(DTE dte, Window outputWindow, OutputWindowPane pane)
		{
			_dte = dte;
			_outputWindow = outputWindow;
			_pane = pane;

			// http://stackoverflow.com/questions/12049362/programmatically-add-add-in-button-to-the-standard-toolbar
			// add a toolbar button to the standard toolbar
			var bar = ((CommandBars)_dte.CommandBars)["Standard"];
			if (bar != null)
			{
				var control = (CommandBarButton)bar.Controls.Add(MsoControlType.msoControlButton, Type.Missing, Type.Missing, Type.Missing, true);
				control.Style = MsoButtonStyle.msoButtonIcon;
				var controlInfo = "BuildOnSave Status";
				control.TooltipText = controlInfo;
				control.Caption = controlInfo;
				control.BeginGroup = true;
				control.Click += (CommandBarButton ctrl, ref bool d) =>
				{
					// side effect: setting visible to true focuses the output window!
					if (!_outputWindow.Visible)
						_outputWindow.Visible = true;
					pane.Activate();
				};
				_barButton_ = control;
			}
			else
			{
				Log.W("failed to add command button, no Standard command bar");
			}

			updateUI();
		}

		public void Dispose()
		{
			_barButton_?.Delete(true);
		}


		public void setBuildStatus(BuildStatus status)
		{
			_status = status;
			_processing = false;
			updateUI();
		}

		public void notifyBeginBuild()
		{
			_processing = true;
			updateUI();
		}

		void updateUI()
		{
			if (_barButton_ == null)
				return;

			using (DevTools.measureBlock("setting image"))
				_barButton_.Picture = getImage(_status, _processing);
		}

		StdPicture getImage(BuildStatus status, bool processing)
		{
			var images = processing ? _statusImagesProcessing : _statusImages;
			var statusAsInt = (int) status;
			if (statusAsInt >= images.Length)
				throw new ArgumentOutOfRangeException(nameof(status), status, null);
			return images[statusAsInt];
		}

		static StdPicture imageToPicture(Image image)
		{
			return PictureConverter.getIPicture(image);
		}


		// http://stackoverflow.com/questions/31324924/vs-2013-sdk-how-to-set-a-commandbarbutton-picture

		sealed class ImageToPictureDispConverter : AxHost
		{
			public ImageToPictureDispConverter() : base("{63109182-966B-4e3c-A8B2-8BC4A88D221C}")
			{}

			public StdPicture getIPicture(Image image)
			{
				return (StdPicture) GetIPictureFromPicture(image);
			}
		}

		static readonly ImageToPictureDispConverter PictureConverter = new ImageToPictureDispConverter();
	}
}
