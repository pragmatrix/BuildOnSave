using System;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Windows.Forms;
using EnvDTE;
using Microsoft.VisualStudio.CommandBars;
using stdole;

namespace BuildOnSave
{

	sealed class DriverUI : IDisposable
	{
		readonly DTE _dte;
		readonly CommandBarButton _barButton_;
		readonly StdPicture[] _statusImages =
		{
			imageToPicture(ImageBuilder.createStatusImage(VSColors.Neutral)),
			imageToPicture(ImageBuilder.createStatusImage(VSColors.Positive)),
			imageToPicture(ImageBuilder.createStatusImage(VSColors.Negative))
		};

		public DriverUI(DTE dte)
		{
			_dte = dte;

			// http://stackoverflow.com/questions/12049362/programmatically-add-add-in-button-to-the-standard-toolbar
			// add a toolbar button to the standard toolbar
			var bar = ((CommandBars)_dte.CommandBars)["Standard"];
			if (bar != null)
			{
				var control = (CommandBarButton)bar.Controls.Add(MsoControlType.msoControlButton, Type.Missing, Type.Missing, Type.Missing, true);
				control.Style = MsoButtonStyle.msoButtonIcon;
				control.TooltipText = "BuildOnSave Status";
				_barButton_ = control;
			}
			else
			{
				Log.W("failed to add command button, no Standard command bar");
			}

			setBuildStatus(BuildStatus.Indeterminate);
		}

		public void Dispose()
		{
			_barButton_?.Delete(true);
		}

		public void setBuildStatus(BuildStatus status)
		{
			if (_barButton_ == null)
				return;

			using (DevTools.measureBlock("setting image"))
				_barButton_.Picture = getImageForBuildStatus(status);
		}

		StdPicture getImageForBuildStatus(BuildStatus status)
		{
			var statusAsInt = (int) status;
			if (statusAsInt >= _statusImages.Length)
				throw new ArgumentOutOfRangeException(nameof(status), status, null);
			return _statusImages[statusAsInt];
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
