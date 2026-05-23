using System;
using System.Drawing;
using FrameShift.Core.Actions;

namespace FrameShift.Core.Helpers;

public static class ImageToPdfGeometry
{
    private const double PointsPerCentimeter = 72.0 / 2.54;
    private const float DefaultContentScale = 0.92f;
    private const float PreviewPadding = 24f;
    private const float MinimumCropVisiblePoints = 24f;
    private const float SnapTolerancePreviewDefault = 12f;

    public readonly record struct PageDefinition(
        string Format,
        double WidthCentimeters,
        double HeightCentimeters,
        double WidthPoints,
        double HeightPoints);

    public readonly record struct AbsoluteRect(
        double X,
        double Y,
        double Width,
        double Height);

    public readonly record struct PreviewResizeHandleRects(
        RectangleF TopLeft,
        RectangleF Top,
        RectangleF TopRight,
        RectangleF Right,
        RectangleF BottomRight,
        RectangleF Bottom,
        RectangleF BottomLeft,
        RectangleF Left);

    public readonly record struct PreviewRotationHandleInfo(
        PointF AxisStart,
        PointF HandleCenter,
        RectangleF HandleBounds);

    public readonly record struct PreviewCropHandleRects(
        RectangleF TopLeft,
        RectangleF Top,
        RectangleF TopRight,
        RectangleF Right,
        RectangleF BottomRight,
        RectangleF Bottom,
        RectangleF BottomLeft,
        RectangleF Left);

    public readonly record struct CropUpdateResult(
        ImageToPdfCropSettings Crop,
        RectangleF VisibleRect);

    public readonly record struct SnapResult(
        RectangleF Rect,
        double? GuideX,
        double? GuideY);

    public static PageDefinition GetPageDefinition(
        string? pageFormat,
        double customPageWidthCm = 0,
        double customPageHeightCm = 0)
    {
        var normalizedFormat = NormalizePageFormat(pageFormat);

        double widthCm;
        double heightCm;

        switch (normalizedFormat)
        {
            case "A3PORTRAIT":
            case "A3":
                widthCm = 29.7;
                heightCm = 42.0;
                break;
            case "A3LANDSCAPE":
                widthCm = 42.0;
                heightCm = 29.7;
                break;
            case "A4LANDSCAPE":
                widthCm = 29.7;
                heightCm = 21.0;
                break;
            case "A4PORTRAIT":
            case "A4":
                widthCm = 21.0;
                heightCm = 29.7;
                normalizedFormat = "A4PORTRAIT";
                break;
            case "CUSTOM":
                if (customPageWidthCm <= 0 || customPageHeightCm <= 0)
                {
                    throw new ArgumentException(MediaActionMessages.ImageToPdfPageSizeInvalid());
                }
                widthCm = customPageWidthCm > 0 ? customPageWidthCm : 21.0;
                heightCm = customPageHeightCm > 0 ? customPageHeightCm : 29.7;
                break;
            default:
                normalizedFormat = "A4PORTRAIT";
                widthCm = 21.0;
                heightCm = 29.7;
                break;
        }

        return new PageDefinition(
            normalizedFormat,
            widthCm,
            heightCm,
            widthCm * PointsPerCentimeter,
            heightCm * PointsPerCentimeter);
    }

    public static double CentimetersToPoints(double centimeters)
    {
        return centimeters * PointsPerCentimeter;
    }

    public static bool IsCustomPageFormat(string? pageFormat)
    {
        return string.Equals(NormalizePageFormat(pageFormat), "CUSTOM", StringComparison.OrdinalIgnoreCase);
    }

    public static ImageToPdfCropSettings NormalizeCrop(ImageToPdfCropSettings? crop)
    {
        var normalized = crop ?? ImageToPdfCropSettings.CreateDefault();
        return new ImageToPdfCropSettings
        {
            Left = Clamp01(normalized.Left),
            Top = Clamp01(normalized.Top),
            Right = Clamp01(normalized.Right),
            Bottom = Clamp01(normalized.Bottom)
        };
    }

    public static bool IsDefaultCrop(ImageToPdfCropSettings? crop)
    {
        var normalized = NormalizeCrop(crop);
        return Math.Abs(normalized.Left) < 0.0001 &&
               Math.Abs(normalized.Top) < 0.0001 &&
               Math.Abs(normalized.Right - 1.0) < 0.0001 &&
               Math.Abs(normalized.Bottom - 1.0) < 0.0001;
    }

    public static string NormalizePageFormat(string? pageFormat)
    {
        var normalizedFormat = string.IsNullOrWhiteSpace(pageFormat)
            ? "A4PORTRAIT"
            : pageFormat.Trim().Replace(" ", string.Empty).Replace("-", string.Empty).Replace("_", string.Empty).ToUpperInvariant();

        return normalizedFormat switch
        {
            "A4" => "A4PORTRAIT",
            "A4PORTRAIT" => "A4PORTRAIT",
            "A4LANDSCAPE" => "A4LANDSCAPE",
            "A3" => "A3PORTRAIT",
            "A3PORTRAIT" => "A3PORTRAIT",
            "A3LANDSCAPE" => "A3LANDSCAPE",
            "CUSTOM" => "CUSTOM",
            _ => "A4PORTRAIT"
        };
    }

    public static RectangleF CreateInitialRectNormalized(Size imageSize, PageDefinition page)
    {
        return CreateFitRectNormalized(imageSize, page, DefaultContentScale);
    }

    public static RectangleF CreateAddedRectNormalized(Size imageSize, PageDefinition page, int existingItemCount)
    {
        var rect = CreateFitRectNormalized(imageSize, page, existingItemCount == 0 ? DefaultContentScale : 0.62f);
        if (existingItemCount <= 0)
        {
            return rect;
        }

        var offsetX = Math.Min(0.04f * existingItemCount, 0.16f);
        var offsetY = Math.Min(0.03f * existingItemCount, 0.12f);
        rect.X += offsetX;
        rect.Y += offsetY;
        return ClampNormalizedRect(rect);
    }

    public static RectangleF CreateFitRectNormalized(Size imageSize, PageDefinition page, float contentScale)
    {
        if (imageSize.Width <= 0 || imageSize.Height <= 0)
        {
            return new RectangleF(0.05f, 0.05f, 0.9f, 0.9f);
        }

        var maxWidthPoints = page.WidthPoints * contentScale;
        var maxHeightPoints = page.HeightPoints * contentScale;
        var scale = Math.Min(maxWidthPoints / imageSize.Width, maxHeightPoints / imageSize.Height);
        var drawWidthPoints = imageSize.Width * scale;
        var drawHeightPoints = imageSize.Height * scale;
        var xPoints = (page.WidthPoints - drawWidthPoints) / 2.0;
        var yPoints = (page.HeightPoints - drawHeightPoints) / 2.0;

        return ClampNormalizedRect(new RectangleF(
            (float)(xPoints / page.WidthPoints),
            (float)(yPoints / page.HeightPoints),
            (float)(drawWidthPoints / page.WidthPoints),
            (float)(drawHeightPoints / page.HeightPoints)));
    }

    public static RectangleF CenterNormalizedRect(RectangleF rect)
    {
        rect = ClampNormalizedRect(rect);
        rect.X = (1f - rect.Width) / 2f;
        rect.Y = (1f - rect.Height) / 2f;
        return ClampNormalizedRect(rect);
    }

    public static RectangleF RemapRectToPage(RectangleF rect, PageDefinition sourcePage, PageDefinition targetPage)
    {
        var sourceAbsolute = ToAbsoluteRect(rect, sourcePage);
        var sourceCenterX = sourcePage.WidthPoints / 2.0;
        var sourceCenterY = sourcePage.HeightPoints / 2.0;
        var targetCenterX = targetPage.WidthPoints / 2.0;
        var targetCenterY = targetPage.HeightPoints / 2.0;
        var scaleX = targetPage.WidthPoints / sourcePage.WidthPoints;
        var scaleY = targetPage.HeightPoints / sourcePage.HeightPoints;
        var uniformScale = Math.Min(scaleX, scaleY);
        var rectCenterX = sourceAbsolute.X + (sourceAbsolute.Width / 2.0);
        var rectCenterY = sourceAbsolute.Y + (sourceAbsolute.Height / 2.0);
        var scaledWidth = sourceAbsolute.Width * uniformScale;
        var scaledHeight = sourceAbsolute.Height * uniformScale;
        var scaledCenterX = targetCenterX + ((rectCenterX - sourceCenterX) * uniformScale);
        var scaledCenterY = targetCenterY + ((rectCenterY - sourceCenterY) * uniformScale);
        var mapped = new RectangleF(
            (float)((scaledCenterX - (scaledWidth / 2.0)) / targetPage.WidthPoints),
            (float)((scaledCenterY - (scaledHeight / 2.0)) / targetPage.HeightPoints),
            (float)(scaledWidth / targetPage.WidthPoints),
            (float)(scaledHeight / targetPage.HeightPoints));

        return ClampNormalizedRect(mapped);
    }

    public static RectangleF RotateRectQuarterTurn(RectangleF rect)
    {
        rect = ClampNormalizedRect(rect);
        var centerX = rect.X + (rect.Width / 2f);
        var centerY = rect.Y + (rect.Height / 2f);
        var rotated = new RectangleF(
            centerX - (rect.Height / 2f),
            centerY - (rect.Width / 2f),
            rect.Height,
            rect.Width);

        return ClampNormalizedRect(rotated);
    }

    public static double NormalizeRotationAngle(double angleDegrees)
    {
        var normalized = angleDegrees % 360.0;
        if (normalized < 0.0)
        {
            normalized += 360.0;
        }

        return normalized;
    }

    public static double SnapRotationAngle(double angleDegrees, double toleranceDegrees)
    {
        var normalized = NormalizeRotationAngle(angleDegrees);
        var targets = new[] { 0.0, 90.0, 180.0, 270.0, 360.0 };
        var bestTarget = normalized;
        var bestDelta = double.MaxValue;

        foreach (var target in targets)
        {
            var delta = Math.Abs(normalized - target);
            if (delta < bestDelta)
            {
                bestDelta = delta;
                bestTarget = target;
            }
        }

        if (bestDelta <= toleranceDegrees)
        {
            return bestTarget >= 360.0 ? 0.0 : bestTarget;
        }

        return angleDegrees;
    }

    public static PointF RotatePoint(PointF point, PointF center, double angleDegrees)
    {
        var radians = angleDegrees * Math.PI / 180.0;
        var cos = Math.Cos(radians);
        var sin = Math.Sin(radians);
        var dx = point.X - center.X;
        var dy = point.Y - center.Y;

        return new PointF(
            (float)(center.X + (dx * cos) - (dy * sin)),
            (float)(center.Y + (dx * sin) + (dy * cos)));
    }

    public static PointF[] GetRotatedPreviewPointsForRect(RectangleF rect, double rotationAngleDegrees)
    {
        var center = new PointF(rect.X + (rect.Width / 2f), rect.Y + (rect.Height / 2f));
        var points = new[]
        {
            new PointF(rect.Left, rect.Top),
            new PointF(rect.Right, rect.Top),
            new PointF(rect.Right, rect.Bottom),
            new PointF(rect.Left, rect.Bottom)
        };

        for (var index = 0; index < points.Length; index++)
        {
            points[index] = RotatePoint(points[index], center, rotationAngleDegrees);
        }

        return points;
    }

    public static PreviewResizeHandleRects GetPreviewResizeHandleRects(RectangleF rect, double rotationAngleDegrees, float handleSize = 10f)
    {
        var points = GetRotatedPreviewPointsForRect(rect, rotationAngleDegrees);
        var half = handleSize / 2f;
        var leftMid = MidPoint(points[0], points[3]);
        var topMid = MidPoint(points[0], points[1]);
        var rightMid = MidPoint(points[1], points[2]);
        var bottomMid = MidPoint(points[2], points[3]);

        return new PreviewResizeHandleRects(
            CreateHandleRect(points[0], half, handleSize),
            CreateHandleRect(topMid, half, handleSize),
            CreateHandleRect(points[1], half, handleSize),
            CreateHandleRect(rightMid, half, handleSize),
            CreateHandleRect(points[2], half, handleSize),
            CreateHandleRect(bottomMid, half, handleSize),
            CreateHandleRect(points[3], half, handleSize),
            CreateHandleRect(leftMid, half, handleSize));
    }

    public static string? GetPreviewResizeHandleHit(RectangleF rect, double rotationAngleDegrees, PointF previewPoint, float handleSize = 10f)
    {
        var handles = GetPreviewResizeHandleRects(rect, rotationAngleDegrees, handleSize);
        foreach (var (name, handleRect) in new (string Name, RectangleF Rect)[]
        {
            ("TopLeft", handles.TopLeft),
            ("Top", handles.Top),
            ("TopRight", handles.TopRight),
            ("Right", handles.Right),
            ("BottomRight", handles.BottomRight),
            ("Bottom", handles.Bottom),
            ("BottomLeft", handles.BottomLeft),
            ("Left", handles.Left)
        })
        {
            if (handleRect.Contains(previewPoint))
            {
                return name;
            }
        }

        return null;
    }

    public static PreviewRotationHandleInfo GetPreviewRotationHandleInfo(
        RectangleF rect,
        double rotationAngleDegrees = 0.0,
        float axisLengthPreview = 22f,
        float handleDiameterPreview = 12f)
    {
        var center = new PointF(rect.X + (rect.Width / 2f), rect.Y + (rect.Height / 2f));
        var topCenter = new PointF(center.X, rect.Top);
        var axisLength = axisLengthPreview;
        var handleCenter = RotatePoint(new PointF(topCenter.X, topCenter.Y - axisLength), center, rotationAngleDegrees);
        var axisStart = RotatePoint(topCenter, center, rotationAngleDegrees);
        var radius = handleDiameterPreview / 2f;

        return new PreviewRotationHandleInfo(
            axisStart,
            handleCenter,
            new RectangleF(handleCenter.X - radius, handleCenter.Y - radius, handleDiameterPreview, handleDiameterPreview));
    }

    public static bool GetPreviewRotationHandleHit(RectangleF rect, double rotationAngleDegrees, PointF previewPoint, float handleDiameterPreview = 12f)
    {
        var info = GetPreviewRotationHandleInfo(rect, rotationAngleDegrees, 22f, handleDiameterPreview);
        return info.HandleBounds.Contains(previewPoint);
    }

    public static RectangleF GetFullRectFromVisibleRectAndCrop(RectangleF visibleRect, ImageToPdfCropSettings? crop)
    {
        var normalized = NormalizeCrop(crop);
        var cropWidthRatio = normalized.Right - normalized.Left;
        var cropHeightRatio = normalized.Bottom - normalized.Top;
        if (cropWidthRatio <= 0.0001 || cropHeightRatio <= 0.0001)
        {
            return visibleRect;
        }

        var fullWidth = visibleRect.Width / cropWidthRatio;
        var fullHeight = visibleRect.Height / cropHeightRatio;
        var fullX = visibleRect.X - (fullWidth * normalized.Left);
        var fullY = visibleRect.Y - (fullHeight * normalized.Top);
        return new RectangleF((float)fullX, (float)fullY, (float)fullWidth, (float)fullHeight);
    }

    public static RectangleF GetCropRectWithinVisibleRect(RectangleF visibleRect, ImageToPdfCropSettings? crop)
    {
        var normalized = NormalizeCrop(crop);
        return new RectangleF(
            visibleRect.X + (visibleRect.Width * (float)normalized.Left),
            visibleRect.Y + (visibleRect.Height * (float)normalized.Top),
            visibleRect.Width * (float)(normalized.Right - normalized.Left),
            visibleRect.Height * (float)(normalized.Bottom - normalized.Top));
    }

    public static PreviewCropHandleRects GetPreviewCropHandleRects(
        RectangleF fullRect,
        ImageToPdfCropSettings? crop,
        double rotationAngleDegrees,
        float handleSize = 10f)
    {
        var normalized = NormalizeCrop(crop);
        var cropRect = GetCropRectWithinFullRect(fullRect, normalized);
        var points = GetRotatedPreviewPointsForRect(cropRect, rotationAngleDegrees);
        var half = handleSize / 2f;
        var leftMid = MidPoint(points[0], points[3]);
        var topMid = MidPoint(points[0], points[1]);
        var rightMid = MidPoint(points[1], points[2]);
        var bottomMid = MidPoint(points[2], points[3]);

        return new PreviewCropHandleRects(
            CreateHandleRect(points[0], half, handleSize),
            CreateHandleRect(topMid, half, handleSize),
            CreateHandleRect(points[1], half, handleSize),
            CreateHandleRect(rightMid, half, handleSize),
            CreateHandleRect(points[2], half, handleSize),
            CreateHandleRect(bottomMid, half, handleSize),
            CreateHandleRect(points[3], half, handleSize),
            CreateHandleRect(leftMid, half, handleSize));
    }

    public static string? GetPreviewCropHandleHit(
        RectangleF fullRect,
        ImageToPdfCropSettings? crop,
        double rotationAngleDegrees,
        PointF previewPoint,
        float handleSize = 10f)
    {
        var handles = GetPreviewCropHandleRects(fullRect, crop, rotationAngleDegrees, handleSize);
        foreach (var (name, handleRect) in new (string Name, RectangleF Rect)[] {
            ("TopLeft", handles.TopLeft),
            ("TopRight", handles.TopRight),
            ("BottomRight", handles.BottomRight),
            ("BottomLeft", handles.BottomLeft),
            ("Left", handles.Left),
            ("Top", handles.Top),
            ("Right", handles.Right),
            ("Bottom", handles.Bottom)
        })
        {
            if (handleRect.Contains(previewPoint))
            {
                return name;
            }
        }

        return null;
    }

    public static CropUpdateResult UpdateCropFromPreviewHandle(
        ImageToPdfCropSettings? originalCrop,
        string handle,
        RectangleF fullRect,
        double rotationAngleDegrees,
        PointF currentPreviewPoint)
    {
        var crop = NormalizeCrop(originalCrop);
        var centerX = fullRect.X + (fullRect.Width / 2f);
        var centerY = fullRect.Y + (fullRect.Height / 2f);
        var localPoint = RotatePoint(currentPreviewPoint, new PointF(centerX, centerY), -rotationAngleDegrees);
        var normalizedX = (localPoint.X - fullRect.X) / fullRect.Width;
        var normalizedY = (localPoint.Y - fullRect.Y) / fullRect.Height;
        var minimumVisibleWidthRatio = GetMinimumCropRatio(fullRect.Width);
        var minimumVisibleHeightRatio = GetMinimumCropRatio(fullRect.Height);

        switch (handle)
        {
            case "Left":
                crop.Left = Math.Max(0.0, Math.Min(crop.Right - minimumVisibleWidthRatio, normalizedX));
                break;
            case "Top":
                crop.Top = Math.Max(0.0, Math.Min(crop.Bottom - minimumVisibleHeightRatio, normalizedY));
                break;
            case "Right":
                crop.Right = Math.Min(1.0, Math.Max(crop.Left + minimumVisibleWidthRatio, normalizedX));
                break;
            case "Bottom":
                crop.Bottom = Math.Min(1.0, Math.Max(crop.Top + minimumVisibleHeightRatio, normalizedY));
                break;
            case "TopLeft":
                crop.Left = Math.Max(0.0, Math.Min(crop.Right - minimumVisibleWidthRatio, normalizedX));
                crop.Top = Math.Max(0.0, Math.Min(crop.Bottom - minimumVisibleHeightRatio, normalizedY));
                break;
            case "TopRight":
                crop.Right = Math.Min(1.0, Math.Max(crop.Left + minimumVisibleWidthRatio, normalizedX));
                crop.Top = Math.Max(0.0, Math.Min(crop.Bottom - minimumVisibleHeightRatio, normalizedY));
                break;
            case "BottomRight":
                crop.Right = Math.Min(1.0, Math.Max(crop.Left + minimumVisibleWidthRatio, normalizedX));
                crop.Bottom = Math.Min(1.0, Math.Max(crop.Top + minimumVisibleHeightRatio, normalizedY));
                break;
            case "BottomLeft":
                crop.Left = Math.Max(0.0, Math.Min(crop.Right - minimumVisibleWidthRatio, normalizedX));
                crop.Bottom = Math.Min(1.0, Math.Max(crop.Top + minimumVisibleHeightRatio, normalizedY));
                break;
            default:
                throw new ArgumentException("Unknown crop handle.", nameof(handle));
        }

        crop = NormalizeCrop(crop);
        return new CropUpdateResult(crop, GetVisibleRectFromFullRectAndCrop(fullRect, crop));
    }

    private static double GetMinimumCropRatio(float fullExtentPoints)
    {
        if (fullExtentPoints <= 0.0001f)
        {
            return 0.05;
        }

        var ratioFromAbsoluteSize = MinimumCropVisiblePoints / fullExtentPoints;
        return Math.Min(0.95, Math.Max(0.05, ratioFromAbsoluteSize));
    }

    public static Rectangle GetBitmapSourceRectFromCrop(Size bitmapSize, ImageToPdfCropSettings? crop)
    {
        var normalized = NormalizeCrop(crop);
        var left = (int)Math.Floor(bitmapSize.Width * normalized.Left);
        var top = (int)Math.Floor(bitmapSize.Height * normalized.Top);
        var right = (int)Math.Ceiling(bitmapSize.Width * normalized.Right);
        var bottom = (int)Math.Ceiling(bitmapSize.Height * normalized.Bottom);

        left = Math.Max(0, Math.Min(bitmapSize.Width - 1, left));
        top = Math.Max(0, Math.Min(bitmapSize.Height - 1, top));
        right = Math.Max(left + 1, Math.Min(bitmapSize.Width, right));
        bottom = Math.Max(top + 1, Math.Min(bitmapSize.Height, bottom));

        return Rectangle.FromLTRB(left, top, right, bottom);
    }

    private static RectangleF GetCropRectWithinFullRect(RectangleF fullRect, ImageToPdfCropSettings crop)
    {
        var cropWidthRatio = crop.Right - crop.Left;
        var cropHeightRatio = crop.Bottom - crop.Top;
        return new RectangleF(
            fullRect.X + (fullRect.Width * (float)crop.Left),
            fullRect.Y + (fullRect.Height * (float)crop.Top),
            fullRect.Width * (float)cropWidthRatio,
            fullRect.Height * (float)cropHeightRatio);
    }

    private static RectangleF GetVisibleRectFromFullRectAndCrop(RectangleF fullRect, ImageToPdfCropSettings crop)
    {
        return GetCropRectWithinFullRect(fullRect, crop);
    }

    public static bool TestPreviewPointInRotatedRect(RectangleF rect, double rotationAngleDegrees, PointF previewPoint)
    {
        var center = new PointF(rect.X + (rect.Width / 2f), rect.Y + (rect.Height / 2f));
        var local = RotatePoint(previewPoint, center, -rotationAngleDegrees);
        return local.X >= rect.Left &&
               local.X <= rect.Right &&
               local.Y >= rect.Top &&
               local.Y <= rect.Bottom;
    }

    public static double GetRotationAngleFromPreviewPoint(RectangleF rect, PointF previewPoint)
    {
        var centerX = rect.X + (rect.Width / 2f);
        var centerY = rect.Y + (rect.Height / 2f);
        var dx = previewPoint.X - centerX;
        var dy = previewPoint.Y - centerY;
        return (Math.Atan2(dy, dx) * 180.0 / Math.PI) + 90.0;
    }

    public static RectangleF MoveNormalizedRect(RectangleF rect, float deltaPreviewX, float deltaPreviewY, RectangleF previewPageRect)
    {
        if (previewPageRect.Width <= 0 || previewPageRect.Height <= 0)
        {
            return ClampNormalizedRect(rect);
        }

        rect.X += deltaPreviewX / previewPageRect.Width;
        rect.Y += deltaPreviewY / previewPageRect.Height;
        return ClampNormalizedRect(rect);
    }

    public static SnapResult ApplySnapToNormalizedRect(
        RectangleF candidateRect,
        IReadOnlyList<RectangleF> allRects,
        int activeIndex,
        RectangleF previewPageRect,
        PageDefinition page,
        float tolerancePreview = SnapTolerancePreviewDefault)
    {
        if (activeIndex < 0 || activeIndex >= allRects.Count)
        {
            return new SnapResult(ClampNormalizedRect(candidateRect), null, null);
        }

        var candidateAbsolute = ToAbsoluteRect(candidateRect, page);
        var tolerancePoints = previewPageRect.Width <= 0f
            ? 0.0
            : tolerancePreview / previewPageRect.Width * page.WidthPoints;

        double? bestDeltaX = null;
        double? bestDeltaY = null;
        double? guideX = null;
        double? guideY = null;

        var candidateRefs = GetAlignmentReferences(candidateAbsolute);

        for (var index = 0; index < allRects.Count; index++)
        {
            if (index == activeIndex)
            {
                continue;
            }

            var otherAbsolute = ToAbsoluteRect(allRects[index], page);
            if (otherAbsolute.Width <= 0.0 || otherAbsolute.Height <= 0.0)
            {
                continue;
            }

            var otherRefs = GetAlignmentReferences(otherAbsolute);

            foreach (var candidateRef in new[] { candidateRefs.Left, candidateRefs.CenterX, candidateRefs.Right })
            {
                foreach (var otherRef in new[] { otherRefs.Left, otherRefs.CenterX, otherRefs.Right })
                {
                    var delta = otherRef - candidateRef;
                    if (Math.Abs(delta) > tolerancePoints)
                    {
                        continue;
                    }

                    if (bestDeltaX is null || Math.Abs(delta) < Math.Abs(bestDeltaX.Value))
                    {
                        bestDeltaX = delta;
                        guideX = otherRef;
                    }
                }
            }

            foreach (var candidateRef in new[] { candidateRefs.Top, candidateRefs.CenterY, candidateRefs.Bottom })
            {
                foreach (var otherRef in new[] { otherRefs.Top, otherRefs.CenterY, otherRefs.Bottom })
                {
                    var delta = otherRef - candidateRef;
                    if (Math.Abs(delta) > tolerancePoints)
                    {
                        continue;
                    }

                    if (bestDeltaY is null || Math.Abs(delta) < Math.Abs(bestDeltaY.Value))
                    {
                        bestDeltaY = delta;
                        guideY = otherRef;
                    }
                }
            }
        }

        var snappedAbsolute = new RectangleF(
            (float)(candidateAbsolute.X + (bestDeltaX ?? 0.0)),
            (float)(candidateAbsolute.Y + (bestDeltaY ?? 0.0)),
            (float)candidateAbsolute.Width,
            (float)candidateAbsolute.Height);

        var snappedNormalized = ClampNormalizedRect(ToNormalizedRect(snappedAbsolute, page));
        return new SnapResult(snappedNormalized, guideX, guideY);
    }

    public static AbsoluteRect ToAbsoluteRect(RectangleF normalizedRect, PageDefinition page)
    {
        var rect = ClampNormalizedRectToBounds(normalizedRect);
        return new AbsoluteRect(
            rect.X * page.WidthPoints,
            rect.Y * page.HeightPoints,
            rect.Width * page.WidthPoints,
            rect.Height * page.HeightPoints);
    }

    public static RectangleF GetPreviewPageRect(Size canvasSize, PageDefinition page)
    {
        var canvasWidth = Math.Max(1, canvasSize.Width);
        var canvasHeight = Math.Max(1, canvasSize.Height);
        var scale = CalculateFitPreviewScale(canvasSize, page);
        return GetPreviewPageRect(canvasWidth, canvasHeight, page, scale);
    }

    public static RectangleF GetPreviewPageRect(Size canvasSize, PageDefinition page, float previewScale)
    {
        return GetPreviewPageRect(canvasSize.Width, canvasSize.Height, page, previewScale);
    }

    public static RectangleF GetPreviewPageRect(int canvasWidth, int canvasHeight, PageDefinition page, float previewScale)
    {
        var width = Math.Max(1f, (float)(page.WidthPoints * previewScale));
        var height = Math.Max(1f, (float)(page.HeightPoints * previewScale));
        var x = Math.Max(PreviewPadding, (canvasWidth - width) / 2f);
        var y = Math.Max(PreviewPadding, (canvasHeight - height) / 2f);
        return new RectangleF(x, y, width, height);
    }

    public static Size GetPreviewCanvasSize(Size viewportSize, PageDefinition page, float previewScale)
    {
        var width = (int)Math.Ceiling((page.WidthPoints * previewScale) + (PreviewPadding * 2f));
        var height = (int)Math.Ceiling((page.HeightPoints * previewScale) + (PreviewPadding * 2f));
        width = Math.Max(Math.Max(1, viewportSize.Width), width);
        height = Math.Max(Math.Max(1, viewportSize.Height), height);
        return new Size(Math.Max(1, width), Math.Max(1, height));
    }

    public static float CalculateFitPreviewScale(Size canvasSize, PageDefinition page)
    {
        var canvasWidth = Math.Max(1, canvasSize.Width);
        var canvasHeight = Math.Max(1, canvasSize.Height);
        var availableWidth = Math.Max(1f, canvasWidth - (PreviewPadding * 2f));
        var availableHeight = Math.Max(1f, canvasHeight - (PreviewPadding * 2f));
        return (float)Math.Min(availableWidth / page.WidthPoints, availableHeight / page.HeightPoints);
    }

    public static RectangleF ToPreviewRect(RectangleF normalizedRect, RectangleF previewPageRect)
    {
        var rect = ClampNormalizedRectToBounds(normalizedRect);
        return new RectangleF(
            previewPageRect.X + (rect.X * previewPageRect.Width),
            previewPageRect.Y + (rect.Y * previewPageRect.Height),
            rect.Width * previewPageRect.Width,
            rect.Height * previewPageRect.Height);
    }

    public static RectangleF ToNormalizedRect(RectangleF previewRect, RectangleF previewPageRect)
    {
        if (previewPageRect.Width <= 0 || previewPageRect.Height <= 0)
        {
            return ClampNormalizedRectToBounds(previewRect);
        }

        return ClampNormalizedRectToBounds(new RectangleF(
            (previewRect.X - previewPageRect.X) / previewPageRect.Width,
            (previewRect.Y - previewPageRect.Y) / previewPageRect.Height,
            previewRect.Width / previewPageRect.Width,
            previewRect.Height / previewPageRect.Height));
    }

    public static RectangleF ToNormalizedRect(RectangleF absoluteRect, PageDefinition page)
    {
        if (page.WidthPoints <= 0 || page.HeightPoints <= 0)
        {
            return ClampNormalizedRectToBounds(absoluteRect);
        }

        return ClampNormalizedRectToBounds(new RectangleF(
            (float)(absoluteRect.X / page.WidthPoints),
            (float)(absoluteRect.Y / page.HeightPoints),
            (float)(absoluteRect.Width / page.WidthPoints),
            (float)(absoluteRect.Height / page.HeightPoints)));
    }

    public static PointF ToAbsolutePoint(PointF previewPoint, RectangleF previewPageRect, PageDefinition page)
    {
        if (previewPageRect.Width <= 0 || previewPageRect.Height <= 0)
        {
            return previewPoint;
        }

        return new PointF(
            (float)(((previewPoint.X - previewPageRect.X) / previewPageRect.Width) * page.WidthPoints),
            (float)(((previewPoint.Y - previewPageRect.Y) / previewPageRect.Height) * page.HeightPoints));
    }

    private static PointF MidPoint(PointF first, PointF second)
    {
        return new PointF((first.X + second.X) / 2f, (first.Y + second.Y) / 2f);
    }

    private static (double Left, double CenterX, double Right, double Top, double CenterY, double Bottom) GetAlignmentReferences(AbsoluteRect rect)
    {
        var left = rect.X;
        var top = rect.Y;
        var right = rect.X + rect.Width;
        var bottom = rect.Y + rect.Height;

        return (
            left,
            left + (rect.Width / 2.0),
            right,
            top,
            top + (rect.Height / 2.0),
            bottom);
    }

    private static RectangleF CreateHandleRect(PointF center, float halfSize, float handleSize)
    {
        return new RectangleF(center.X - halfSize, center.Y - halfSize, handleSize, handleSize);
    }

    private static double Clamp01(double value)
    {
        return Math.Max(0.0, Math.Min(1.0, value));
    }

    public static RectangleF ClampNormalizedRect(RectangleF rect)
    {
        var width = rect.Width;
        var height = rect.Height;

        if (width < 0.1f)
        {
            width = 0.1f;
        }
        if (height < 0.1f)
        {
            height = 0.1f;
        }
        return ClampNormalizedRectToBounds(new RectangleF(rect.X, rect.Y, width, height));
    }

    public static RectangleF ClampNormalizedRectToBounds(RectangleF rect)
    {
        var width = rect.Width;
        var height = rect.Height;

        if (width < 0f)
        {
            width = 0f;
        }
        if (height < 0f)
        {
            height = 0f;
        }
        if (width > 1f)
        {
            width = 1f;
        }
        if (height > 1f)
        {
            height = 1f;
        }

        var x = rect.X;
        var y = rect.Y;

        if (x < 0f)
        {
            x = 0f;
        }
        if (y < 0f)
        {
            y = 0f;
        }
        if (x + width > 1f)
        {
            x = 1f - width;
        }
        if (y + height > 1f)
        {
            y = 1f - height;
        }

        return new RectangleF(x, y, width, height);
    }

    public static int NormalizeQuarterTurns(int rotationQuarterTurns)
    {
        var normalized = rotationQuarterTurns % 4;
        if (normalized < 0)
        {
            normalized += 4;
        }

        return normalized;
    }
}
