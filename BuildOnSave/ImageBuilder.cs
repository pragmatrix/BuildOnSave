using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace BuildOnSave
{
	static class ImageBuilder
	{
		static readonly SizeF StatusImageSize = new SizeF(32,32);
		const float StatusStrokeThickness = 2;

		public static Image createStatusImage(Color fillColor)
		{
			var bm = new Bitmap((int)StatusImageSize.Width, (int)StatusImageSize.Height, PixelFormat.Format32bppArgb);
			using (var graphics = Graphics.FromImage(bm))
			{
				var baseRect = new RectangleF(new PointF(0,0), new SizeF(32, 32));

				graphics.SmoothingMode = SmoothingMode.HighQuality;
				graphics.TranslateTransform(-0.5f, -0.5f);
				graphics.FillEllipse(new SolidBrush(fillColor), RectangleF.Inflate(baseRect, -StatusStrokeThickness/2, -StatusStrokeThickness/2));
				graphics.DrawEllipse(new Pen(VSColors.Outline, StatusStrokeThickness), RectangleF.Inflate(baseRect, -StatusStrokeThickness/2, -StatusStrokeThickness/2));
			}
			return bm;
		}
	}

	// https://msdn.microsoft.com/en-us/library/mt186350.aspx
	static class VSColors
	{
		public static readonly Color Positive = Color.FromArgb(0x38, 0x8a, 0x34);
		public static readonly Color Negative = Color.FromArgb(0xa1, 0x26, 0x0d);
		public static readonly Color Neutral = Color.FromArgb(0x00, 0x53, 0x9c);
		public static readonly Color Create = Color.FromArgb(0xc2, 0x7d, 0x1a);
		public static readonly Color Outline = Color.FromArgb(0xf6, 0xf6, 0xf6);

		public static class Notification
		{
			public static readonly Color Neutral = Color.FromArgb(0x1b, 0xa1, 0xe2);
			public static readonly Color Positive = Color.FromArgb(0x33, 0x99, 0x33);
			public static readonly Color Negative = Color.FromArgb(0xe5, 0x14, 0x00);
			public static readonly Color Warning = Color.FromArgb(0xff, 0xcc, 0x00);
		}
	}
}
