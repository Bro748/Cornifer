﻿using System;

namespace Cornifer.Structures
{
    [Flags]
    public enum RainWorldFeatures 
    {
        None = 0,
        Legacy = 1,
        Remix = 2,
        Downpour = 4,

        Steam = 8,
        CRS = 16,

		Watcher = 32,

        All = Legacy | Remix | Downpour | Steam | CRS | Watcher ,
    }
}
