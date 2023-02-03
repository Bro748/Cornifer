﻿using Cornifer.Renderers;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Cornifer
{
    public interface IDrawable
    {
        public void Draw(Renderer renderer);
    }
}
