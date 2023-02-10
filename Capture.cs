﻿using Cornifer.Renderers;
using Microsoft.Xna.Framework;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using System;
using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Cornifer
{
    public static class Capture
    {
        public static Image<Rgba32> CaptureMap()
        {
            Vector2 tl = Vector2.Zero;
            Vector2 br = Vector2.Zero;

            foreach (MapObject obj in Main.WorldObjectLists)
            {
                if (!obj.Active)
                    continue;

                tl.X = Math.Min(tl.X, obj.WorldPosition.X);
                tl.Y = Math.Min(tl.Y, obj.WorldPosition.Y);

                br.X = Math.Max(br.X, obj.WorldPosition.X + obj.Size.X);
                br.Y = Math.Max(br.Y, obj.WorldPosition.Y + obj.Size.Y);
            }

            int width = (int)(br.X - tl.X);
            int height = (int)(br.Y - tl.Y);

            Image<Rgba32> image = new(width, height);

            CaptureRenderer renderer = new(image)
            {
                Position = tl
            };

            Main.DrawMap(renderer);

            return image;
        }
    }
}
