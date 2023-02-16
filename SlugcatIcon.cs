﻿using Cornifer.Renderers;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Cornifer
{
    public class SlugcatIcon : SelectableIcon
    {
        public int Id;

        public bool ForceSlugcatIcon;

        public override int ShadeSize => InterfaceState.DrawSLugcatDiamond.Value ? 0 : 1;
        public override bool Active => (InterfaceState.DrawSLugcatIcons.Value || ForceSlugcatIcon) && base.Active;
        public override Vector2 Size => InterfaceState.DrawSLugcatDiamond.Value && !ForceSlugcatIcon ? new(9) : new(8);

        public SlugcatIcon(string name)
        {
            Name = name;
        }

        public override void DrawIcon(Renderer renderer)
        {
            Rectangle frame = InterfaceState.DrawSLugcatDiamond.Value && !ForceSlugcatIcon ? new(Id*9, 8, 9, 9) : new(Id*8, 0, 8, 8);

            renderer.DrawTexture(Content.SlugcatIcons, WorldPosition, frame);
        }
    }
}
