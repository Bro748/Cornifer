﻿using Cornifer.Renderers;
using Cornifer.UI.Elements;
using Microsoft.Xna.Framework;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Cornifer
{
    public abstract class MapObject
    {
        public bool ParentSelected => Parent != null && (Parent.Selected || Parent.ParentSelected);
        public bool Selected => Main.SelectedObjects.Contains(this);

        public virtual bool Active { get; set; } = true;
        public virtual Vector2 ParentPosition { get; set; }
        public virtual Vector2 Size { get; }

        public MapObject? Parent { get; set; }

        public virtual string? Name { get; set; }

        internal UIElement? ConfigCache { get; set; }

        public Vector2 WorldPosition
        {
            get => Parent is null ? ParentPosition : Parent.WorldPosition + ParentPosition;
            set
            {
                if (Parent is not null)
                    ParentPosition = value - Parent.WorldPosition;
                else
                    ParentPosition = value;
            }
        }

        public MapObjectCollection Children { get; }

        public MapObject()
        {
            Children = new(this);
        }

        public void Draw(Renderer renderer)
        {
            if (!Active)
                return;

            DrawSelf(renderer);

            foreach (MapObject child in Children)
                child.Draw(renderer);
        }

        protected abstract void DrawSelf(Renderer renderer);

        public UIElement? Config
        {
            get => ConfigCache ??= BuildConfig();
        }

        private UIElement? BuildConfig()
        {
            return BuildInnerConfig();
        }

        protected virtual UIElement? BuildInnerConfig() => null;

        public static MapObject? FindSelectableAtPos(IEnumerable<MapObject> objects, Vector2 pos, bool searchChildren)
        {
            foreach (MapObject obj in objects.SmartReverse())
            {
                if (searchChildren)
                {
                    MapObject? child = FindSelectableAtPos(obj.Children, pos, true);
                    if (child is not null)
                        return child;
                }

                if (obj.WorldPosition.X <= pos.X
                 && obj.WorldPosition.Y <= pos.Y
                 && obj.WorldPosition.X + obj.Size.X > pos.X
                 && obj.WorldPosition.Y + obj.Size.Y > pos.Y)
                    return obj;
            }
            return null;
        }
        public static IEnumerable<MapObject> FindIntersectingSelectables(IEnumerable<MapObject> objects, Vector2 tl, Vector2 br, bool searchChildren)
        {
            foreach (MapObject obj in objects.SmartReverse())
            {
                if (searchChildren)
                    foreach (MapObject child in FindIntersectingSelectables(obj.Children, tl, br, true))
                        yield return child;

                bool intersects = obj.WorldPosition.X < br.X
                    && tl.X < obj.WorldPosition.X + obj.Size.X
                    && obj.WorldPosition.Y < br.Y
                    && tl.Y < obj.WorldPosition.Y + obj.Size.Y;
                if (intersects)
                    yield return obj;
            }
        }

        public class MapObjectCollection : ICollection<MapObject>
        {
            List<MapObject> Objects = new();
            MapObject Parent;

            public MapObjectCollection(MapObject parent)
            {
                Parent = parent;
            }

            public int Count => Objects.Count;
            public bool IsReadOnly => false;

            public void Add(MapObject item)
            {
                item.Parent?.Children.Remove(item);
                item.Parent = Parent;
                Objects.Add(item);
            }

            public void Clear()
            {
                foreach (MapObject obj in Objects)
                    obj.Parent = null;

                Objects.Clear();
            }

            public bool Remove(MapObject item)
            {
                item.Parent = null;
                return Objects.Remove(item);
            }

            public bool Contains(MapObject item)
            {
                return Objects.Contains(item);
            }

            public void CopyTo(MapObject[] array, int arrayIndex)
            {
                Objects.CopyTo(array, arrayIndex);
            }

            public IEnumerator<MapObject> GetEnumerator()
            {
                return Objects.GetEnumerator();
            }

            IEnumerator IEnumerable.GetEnumerator()
            {
                return Objects.GetEnumerator();
            }
        }
    }
}