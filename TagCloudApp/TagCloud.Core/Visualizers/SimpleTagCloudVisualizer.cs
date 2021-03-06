﻿using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using TagCloud.Core.Layouters;
using TagCloud.Core.Painters;
using TagCloud.Core.Settings.Interfaces;
using TagCloud.Core.Util;

namespace TagCloud.Core.Visualizers
{
    public class SimpleTagCloudVisualizer : ITagCloudVisualizer
    {
        private readonly ICloudLayouter layouter;
        private readonly IPainter painter;
        private readonly IVisualizingSettings settings;

        private Bitmap bitmap;
        private Graphics graphics;

        public SimpleTagCloudVisualizer(IVisualizingSettings settings, ICloudLayouter layouter, IPainter painter)
        {
            this.settings = settings;
            this.layouter = layouter;
            this.painter = painter;
        }

        public Result<Bitmap> Render(IEnumerable<TagStat> tagStats)
        {
            return Initialize()
                .Then(none => layouter.RefreshWith(settings.CenterPoint))
                .Then(none => painter.SetBackgroundColorFor(graphics))
                .Then(none => GetResultTags(tagStats))
                .Then(tags => tags.ApplyForeach(graphics.DrawTag))
                .Then(_ => bitmap);
        }

        private Result<None> Initialize()
        {
            return Result.OfAction(() => {
                bitmap = new Bitmap(settings.Width, settings.Height);
                graphics = Graphics.FromImage(bitmap);
            }).AppendToErrorFromLeft($"Can't create bitmap from these settings: width={settings.Width}, height={settings.Height}.\n");
        }

        private (double fontSizeMultiplier, double averageRepeatsCount) GetFontSizeMultiplierAndAverageRepeatsCount(
            IEnumerable<TagStat> tagStats)
        {
            var minRepeatsCount = int.MaxValue;
            var maxRepeatsCount = int.MinValue;
            foreach (var tagStat in tagStats)
            {
                if (tagStat.RepeatsCount < minRepeatsCount)
                    minRepeatsCount = tagStat.RepeatsCount;
                if (tagStat.RepeatsCount > maxRepeatsCount)
                    maxRepeatsCount = tagStat.RepeatsCount;
            }

            var fontSizeMultiplier = (double) (settings.MaxFontSize - settings.MinFontSize + 1) /
                                     (maxRepeatsCount - minRepeatsCount + 1);
            var averageRepeatsCount = (double) (minRepeatsCount + maxRepeatsCount) / 2;
            return (fontSizeMultiplier, averageRepeatsCount);
        }

        private Result<IEnumerable<Tag>> GetResultTags(IEnumerable<TagStat> tagStats)
        {
            if (settings.MinFontSize < 0 || settings.MaxFontSize < settings.MinFontSize)
                return Result.Fail<IEnumerable<Tag>>("MinFontSize and MaxFontSize should satisfy next inequality:\n" +
                                                     "\t0 <= MinFontSize <= MaxFontSize");

            var tagStatsList = tagStats.ToList();
            var (fontSizeMultiplier, averageRepeatsCount) = GetFontSizeMultiplierAndAverageRepeatsCount(tagStatsList);

            var resTags = new List<Tag>();
            foreach (var tagStat in tagStatsList)
            {
                var tagResult = CreateTagFrom(tagStat, fontSizeMultiplier, averageRepeatsCount);
                if (tagResult.IsSuccess)
                    resTags.Add(tagResult.Value);
                else
                    return Result.Fail<IEnumerable<Tag>>(tagResult.Error);
            }

            return painter.PaintTags(resTags)
                .Then(none => (Result<IEnumerable<Tag>>)resTags);
        }

        private Result<Tag> CreateTagFrom(TagStat tagStat, double fontSizeMultiplier, double averageWordsCount)
        {
            var fontSizeDelta = (tagStat.RepeatsCount - averageWordsCount) * fontSizeMultiplier;
            Font resFont;
            if (settings.DefaultFont != null)
                resFont = settings.DefaultFont.WithModifiedFontSizeOf((float) fontSizeDelta);
            else
                return Result.Fail<Tag>("Incorrect font given");

            return Result
                .Of(() => graphics.MeasureString(tagStat.Word, resFont))
                .Then(stringSize => layouter.PutNextRectangle(stringSize))
                .Then(tagPlace => new RectangleF(new PointF(0, 0), bitmap.Size).Contains(tagPlace)
                    ? new Tag(tagStat, resFont, tagPlace)
                    : Result.Fail<Tag>($"Can't visualize all tags inside bitmap of size {bitmap.Size}"));
        }
    }
}