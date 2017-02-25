﻿// Copyright (c) 2007-2017 ppy Pty Ltd <contact@ppy.sh>.
// Licensed under the MIT Licence - https://raw.githubusercontent.com/ppy/osu-framework/master/LICENCE

using osu.Framework.Lists;
using System.Collections.Generic;
using System;
using System.Diagnostics;
using OpenTK;
using osu.Framework.Graphics.Primitives;
using osu.Framework.Graphics.OpenGL;
using OpenTK.Graphics;
using osu.Framework.Graphics.Shaders;
using osu.Framework.Extensions.IEnumerableExtensions;
using osu.Framework.Graphics.Colour;
using osu.Framework.Allocation;
using osu.Framework.Graphics.Transformations;
using osu.Framework.Timing;

namespace osu.Framework.Graphics.Containers
{
    public class Container : Container<Drawable>
    { }

    /// <summary>
    /// A drawable which can have children added externally.
    /// </summary>
    public partial class Container<T> : Drawable, IContainerEnumerable<T>, IContainerCollection<T>
        where T : Drawable
    {
        private bool masking;

        /// <summary>
        /// If enabled, only the portion of children that falls within this container's
        /// shape is drawn to the screen.
        /// </summary>
        public bool Masking
        {
            get { return masking; }
            set
            {
                if (masking == value)
                    return;

                masking = value;
                Invalidate(Invalidation.DrawNode);
            }
        }

        private float maskingSmoothness = 1;

        /// <summary>
        /// Determines over how many pixels the alpha component smoothly fades out.
        /// Only has an effect when <see cref="Masking"/> is true.
        /// </summary>
        public float MaskingSmoothness
        {
            get { return maskingSmoothness; }
            set
            {
                //must be above zero to avoid a div-by-zero in the shader logic.
                value = Math.Max(0.01f, value);

                if (maskingSmoothness == value)
                    return;

                maskingSmoothness = value;
                Invalidate(Invalidation.DrawNode);
            }
        }

        private float cornerRadius;

        /// <summary>
        /// Determines how large of a radius is masked away around the corners.
        /// Only has an effect when <see cref="Masking"/> is true.
        /// </summary>
        public virtual float CornerRadius
        {
            get { return cornerRadius; }
            set
            {
                if (cornerRadius == value)
                    return;

                cornerRadius = value;
                Invalidate(Invalidation.DrawNode);
            }
        }

        private float borderThickness;

        /// <summary>
        /// Determines how thick of a border to draw around the inside of the masked region.
        /// Only has an effect when <see cref="Masking"/> is true.
        /// The border only is drawn on top of children of type <see cref="Sprite"/>.
        /// </summary>
        /// <remarks>
        /// Drawing borders is optimized heavily into our sprite shaders. As a consequence
        /// borders are only drawn correctly on top of quad-shaped children using our sprite
        /// shaders.
        /// </remarks>
        public virtual float BorderThickness
        {
            get { return borderThickness; }
            set
            {
                if (borderThickness == value)
                    return;

                borderThickness = value;
                Invalidate(Invalidation.DrawNode);
            }
        }

        private SRGBColour borderColour = Color4.Black;

        /// <summary>
        /// Determines the color of the border controlled by <see cref="BorderThickness"/>.
        /// Only has an effect when <see cref="Masking"/> is true.
        /// </summary>
        public virtual SRGBColour BorderColour
        {
            get { return borderColour; }
            set
            {
                if (borderColour.Equals(value))
                    return;

                borderColour = value;
                Invalidate(Invalidation.DrawNode);
            }
        }

        private EdgeEffect edgeEffect;

        /// <summary>
        /// Determines an edge effect of this container.
        /// Edge effects are e.g. glow or a shadow.
        /// Only has an effect when <see cref="Masking"/> is true.
        /// </summary>
        public virtual EdgeEffect EdgeEffect
        {
            get { return edgeEffect; }
            set
            {
                if (edgeEffect.Equals(value))
                    return;

                edgeEffect = value;
                Invalidate(Invalidation.DrawNode);
            }
        }

        public Container(LifetimeList<T> lifetimeList = null)
        {
            internalChildren = lifetimeList ?? new LifetimeList<T>(DepthComparer);
            internalChildren.Removed += obj =>
            {
                if (obj.DisposeOnDeathRemoval) obj.Dispose();
            };
        }

        private ContainerDrawNodeSharedData containerDrawNodeSharedData = new ContainerDrawNodeSharedData();
        private Shader shader;

        protected override DrawNode CreateDrawNode() => new ContainerDrawNode();

        protected override void ApplyDrawNode(DrawNode node)
        {
            ContainerDrawNode n = (ContainerDrawNode)node;

            if (!Masking && (CornerRadius != 0.0f || BorderThickness != 0.0f || EdgeEffect.Type != EdgeEffectType.None))
                throw new InvalidOperationException("Can not have rounded corners, border effects, or edge effects if masking is disabled.");

            Vector3 scale = DrawInfo.MatrixInverse.ExtractScale();

            n.MaskingInfo = !Masking ? (MaskingInfo?)null : new MaskingInfo
            {
                ScreenSpaceAABB = ScreenSpaceDrawQuad.AABB,
                MaskingRect = DrawRectangle,
                ToMaskingSpace = DrawInfo.MatrixInverse,
                CornerRadius = CornerRadius,
                BorderThickness = BorderThickness,
                BorderColour = BorderColour,
                // We are setting the linear blend range to the approximate size of a _pixel_ here.
                // This results in the optimal trade-off between crispness and smoothness of the
                // edges of the masked region according to sampling theory.
                BlendRange = MaskingSmoothness * (scale.X + scale.Y) / 2,
                AlphaExponent = 1,
            };

            n.EdgeEffect = EdgeEffect;

            n.ScreenSpaceMaskingQuad = null;
            n.Shared = containerDrawNodeSharedData;

            n.Shader = shader;

            base.ApplyDrawNode(node);
        }

        public override bool HandleInput => true;

        /// <summary>
        /// We only want to add to <see cref="internalChildren"/> once we are loaded.
        /// This list holds children-to-be-added until we are loaded.
        /// </summary>
        private List<T> pendingChildren;

        public IEnumerable<T> Children
        {
            get
            {
                return Content != this ? Content.Children : internalChildren;
            }

            set
            {
                Clear();
                Add(value);
            }
        }

        private LifetimeList<T> internalChildren;

        public IEnumerable<T> InternalChildren
        {
            get { return internalChildren; }

            set
            {
                Clear();
                AddInternal(value);
            }
        }

        protected IEnumerable<T> AliveInternalChildren => internalChildren.AliveItems;

        private MarginPadding padding;
        public MarginPadding Padding
        {
            get { return padding; }
            set
            {
                if (padding.Equals(value)) return;

                padding = value;

                foreach (T c in internalChildren)
                    c.Invalidate(Invalidation.Geometry);
            }
        }

        /// <summary>
        /// The size of the positional coordinate space revealed to <see cref="InternalChildren"/>.
        /// </summary>
        public Vector2 ChildSize => DrawSize - new Vector2(Padding.TotalHorizontal, Padding.TotalVertical);

        /// <summary>
        /// Positional offset applied to <see cref="InternalChildren"/>.
        /// </summary>
        public Vector2 ChildOffset => new Vector2(Padding.Left, Padding.Top);

        /// <summary>
        /// Adds a child to this container. This amount to adding a child to <see cref="Content"/>'s
        /// <see cref="Children"/> list, recursing until <see cref="Content"/> == this.
        /// </summary>
        public virtual void Add(T drawable)
        {
            if (drawable == Content)
                throw new InvalidOperationException("Content may not be added to itself.");

            if (Content == this)
                AddInternal(drawable);
            else
                Content.Add(drawable);
        }

        /// <summary>
        /// Adds a collection of children. This is equivalent to calling <see cref="Add"/> on
        /// each element of the collection in order.
        /// </summary>
        public void Add(IEnumerable<T> collection)
        {
            foreach (T d in collection)
                Add(d);
        }

        public virtual bool Remove(T drawable)
        {
            if (drawable == null)
                return false;

            if (Content != this)
                return Content.Remove(drawable);

            bool result = internalChildren.Remove(drawable);
            drawable.Parent = null;

            if (!result) return false;

            drawable.Invalidate();

            if (AutoSizeAxes != Axes.None)
                InvalidateFromChild(Invalidation.Geometry, drawable);

            return true;
        }

        public int RemoveAll(Predicate<T> match)
        {
            List<T> toRemove = internalChildren.FindAll(match);
            foreach (T removable in toRemove)
                Remove(removable);

            return toRemove.Count;
        }

        public void Remove(IEnumerable<T> range)
        {
            if (range == null)
                return;

            foreach (T p in range)
                Remove(p);
        }

        public virtual void Clear(bool dispose = true)
        {
            if (Content != null && Content != this)
            {
                Content.Clear(dispose);
                return;
            }

            foreach (T t in internalChildren)
            {
                if (dispose)
                {
                    //cascade disposal
                    (t as IContainer)?.Clear();
                    t.Dispose();
                }

                Trace.Assert(t.Parent == null);
            }

            internalChildren.Clear();

            Invalidate(Invalidation.Geometry);
        }

        /// <summary>
        /// Adds a child to this container's internal children list. Ignores <see cref="Content"/>.
        /// </summary>
        protected void AddInternal(T drawable)
        {
            if (drawable == null)
                throw new ArgumentNullException("null-Drawables may not be added to Containers.", nameof(drawable));
            if (drawable == this)
                throw new InvalidOperationException("Container may not be added to itself.");

            if (LoadState == LoadState.NotLoaded)
            {
                if (pendingChildren == null)
                    pendingChildren = new List<T>();
                pendingChildren.Add(drawable);
            }
            else
            {
                if (drawable.IsLoaded)
                    drawable.Parent = this;

                internalChildren.Add(drawable);
            }

            if (AutoSizeAxes != Axes.None)
                InvalidateFromChild(Invalidation.Geometry, drawable);
        }

        /// <summary>
        /// Adds a collection of children internally. This is equivalent to calling
        /// <see cref="AddInternal"/> on each element of the collection in order.
        /// </summary>
        protected void AddInternal(IEnumerable<T> collection)
        {
            foreach (T d in collection)
                AddInternal(d);
        }

        protected virtual Container<T> Content => this;

        /// <summary>
        /// Updates the life status of children according to their IsAlive property.
        /// </summary>
        /// <returns>True iff the life status of at least one child changed.</returns>
        protected virtual bool UpdateChildrenLife()
        {
            bool changed = internalChildren.Update(Time);

            if (changed && AutoSizeAxes != Axes.None)
                autoSize.Invalidate();

            return changed;
        }

        internal override void UpdateClock(IFrameBasedClock clock)
        {
            if (Clock == clock)
                return;

            base.UpdateClock(clock);
            foreach (T child in InternalChildren)
                child.UpdateClock(Clock);
        }

        protected internal override bool UpdateSubTree()
        {
            if (!base.UpdateSubTree()) return false;

            // We update our children's life even if we are invisible.
            // Note, that this does not propagate down and may need
            // generalization in the future.
            UpdateChildrenLife();

            // If we are not present then there is never a reason to check
            // for children, as they should never affect our present status.
            if (!IsPresent || !RequiresChildrenUpdate) return false;

            foreach (T child in internalChildren.AliveItems)
                if (child.IsLoaded) child.UpdateSubTree();

            UpdateAfterChildren();

            if (AutoSizeAxes != Axes.None)
                updateAutoSize();
            return true;
        }

        [BackgroundDependencyLoader(true)]
        private void load(Game game, ShaderManager shaders)
        {
            if (shader == null)
                shader = shaders?.Load(VertexShaderDescriptor.Texture2D, FragmentShaderDescriptor.TextureRounded);

            internalChildren.LoadRequested += i =>
            {
                i.Load(game);
                i.Parent = this;
            };

            if (pendingChildren != null)
            {
                AddInternal(pendingChildren);
                pendingChildren = null;
            }
        }

        /// <summary>
        /// Specifies whether this Container requires an update of its children.
        /// If the return value is false, then children are not updated and
        /// <see cref="UpdateAfterChildren"/> is not called.
        /// </summary>
        protected virtual bool RequiresChildrenUpdate => !IsMaskedAway || !autoSize.IsValid;

        public virtual void InvalidateFromChild(Invalidation invalidation, IDrawable source)
        {
            if (AutoSizeAxes == Axes.None) return;

            if ((invalidation & Invalidation.Geometry) > 0)
                autoSize.Invalidate();
        }

        public override bool Invalidate(Invalidation invalidation = Invalidation.All, Drawable source = null, bool shallPropagate = true)
        {
            if (!base.Invalidate(invalidation, source, shallPropagate))
                return false;

            if (!shallPropagate) return true;

            // This way of looping turns out to be slightly faster than a foreach
            // or directly indexing a SortedList<T>. This part of the code is often
            // hot, so an optimization like this makes sense here.
            List<T> current = internalChildren;
            for (int i = 0; i < current.Count; ++i)
            {
                T c = current[i];
                Debug.Assert(c != source);

                Invalidation childInvalidation = invalidation;
                if (c.RelativeSizeAxes == Axes.None)
                    childInvalidation = childInvalidation & ~Invalidation.SizeInParentSpace;

                c.Invalidate(childInvalidation, this);
            }

            return true;
        }

        /// <summary>
        /// Perform any layout changes just before autosize is calculated.		
        /// </summary>		
        protected virtual void UpdateAfterChildren()
        {
        }

        public override Axes RelativeSizeAxes
        {
            get { return base.RelativeSizeAxes; }
            set
            {
                if ((AutoSizeAxes & value) != 0)
                    throw new InvalidOperationException("No axis can be relatively sized and automatically sized at the same time.");

                base.RelativeSizeAxes = value;
            }
        }

        protected virtual bool CanBeFlattened => !Masking;

        private const int amount_children_required_for_masking_check = 2;

        /// <summary>
        /// This function adds all children's DrawNodes to a target List, flattening the children of certain types
        /// of container subtrees for optimization purposes.
        /// </summary>
        /// <param name="treeIndex">The index of the currently in-use DrawNode tree.</param>
        /// <param name="j">The running index into the target List.</param>
        /// <param name="parentContainer">The container whose children's DrawNodes to add.</param>
        /// <param name="target">The target list to fill with DrawNodes.</param>
        /// <param name="maskingBounds">The masking bounds. Children lying outside of them should be ignored.</param>
        private static void addFromContainer(int treeIndex, ref int j, Container<T> parentContainer, List<DrawNode> target, RectangleF maskingBounds)
        {
            List<T> current = parentContainer.internalChildren.AliveItems;
            for (int i = 0; i < current.Count; ++i)
            {
                Drawable drawable = current[i];

                while (drawable != (drawable = drawable.Original)) { }

                if (!drawable.IsPresent)
                    continue;

                // We are consciously missing out on potential flattening (due to lack of covariance)
                // in order to be able to let this loop be over integers instead of using
                // IContainerEnumerable<Drrawable>.AliveChildren which measures to be a _major_ slowdown.
                Container<T> container = drawable as Container<T>;
                if (container?.CanBeFlattened == true)
                {
                    // The masking check is overly expensive (requires creation of ScreenSpaceDrawQuad)
                    // when only few children exist.
                    container.IsMaskedAway = container.internalChildren.AliveItems.Count >= amount_children_required_for_masking_check &&
                        !maskingBounds.IntersectsWith(drawable.ScreenSpaceDrawQuad.AABBFloat);

                    if (!container.IsMaskedAway)
                        addFromContainer(treeIndex, ref j, container, target, maskingBounds);

                    continue;
                }

                drawable.IsMaskedAway = !maskingBounds.IntersectsWith(drawable.ScreenSpaceDrawQuad.AABBFloat);
                if (drawable.IsMaskedAway)
                    continue;

                DrawNode next = drawable.GenerateDrawNodeSubtree(treeIndex, maskingBounds);
                if (next == null)
                    continue;

                if (j < target.Count)
                    target[j] = next;
                else
                    target.Add(next);

                j++;
            }
        }

        protected internal override DrawNode GenerateDrawNodeSubtree(int treeIndex, RectangleF bounds)
        {
            // No need for a draw node at all if there are no children and we are not glowing.
            if (internalChildren.AliveItems.Count == 0 && CanBeFlattened)
                return null;

            ContainerDrawNode cNode = base.GenerateDrawNodeSubtree(treeIndex, bounds) as ContainerDrawNode;
            if (cNode == null)
                return null;

            RectangleF childBounds = bounds;
            // If we are going to render a buffered container we need to make sure no children get masked away,
            // even if they are off-screen.
            if (this is BufferedContainer)
                childBounds = ScreenSpaceDrawQuad.AABBFloat;
            else if (Masking)
                childBounds.Intersect(ScreenSpaceDrawQuad.AABBFloat);

            if (cNode.Children == null)
                cNode.Children = new List<DrawNode>(internalChildren.AliveItems.Count);

            List<DrawNode> target = cNode.Children;

            int j = 0;
            addFromContainer(treeIndex, ref j, this, target, childBounds);

            if (j < target.Count)
                target.RemoveRange(j, target.Count - j);

            return cNode;
        }

        public override void ClearTransformations(bool propagateChildren = false)
        {
            base.ClearTransformations(propagateChildren);

            if (propagateChildren)
                foreach (var c in internalChildren) c.ClearTransformations(true);
        }

        public override Drawable Delay(double duration, bool propagateChildren = false)
        {
            if (duration == 0) return this;

            base.Delay(duration, propagateChildren);

            if (propagateChildren)
                foreach (var c in internalChildren) c.Delay(duration, true);
            return this;
        }

        public override void Flush(bool propagateChildren = false, Type flushType = null)
        {
            base.Flush(propagateChildren, flushType);

            if (propagateChildren)
                foreach (var c in internalChildren) c.Flush(true, flushType);
        }

        public override Drawable DelayReset()
        {
            base.DelayReset();
            foreach (var c in internalChildren) c.DelayReset();

            return this;
        }

        public int IndexOf(T drawable)
        {
            return internalChildren.IndexOf(drawable);
        }

        public bool Contains(T drawable)
        {
            return IndexOf(drawable) >= 0;
        }

        public override bool Contains(Vector2 screenSpacePos)
        {
            float cornerRadius = CornerRadius;

            // Select a cheaper contains method when we don't need rounded edges.
            if (!Masking || cornerRadius == 0.0f)
                return base.Contains(screenSpacePos);
            return DrawRectangle.Shrink(cornerRadius).DistanceSquared(ToLocalSpace(screenSpacePos)) <= cornerRadius * cornerRadius;
        }

        public override RectangleF BoundingBox
        {
            get
            {
                float cornerRadius = CornerRadius;
                if (!Masking || cornerRadius == 0.0f)
                    return base.BoundingBox;

                RectangleF drawRect = LayoutRectangle.Shrink(cornerRadius);

                // Inflate bounding box in parent space by the half-size of the bounding box of the
                // ellipse obtained by transforming the unit circle into parent space.
                Vector2 offset = ToParentSpace(Vector2.Zero);
                Vector2 u = ToParentSpace(new Vector2(cornerRadius, 0)) - offset;
                Vector2 v = ToParentSpace(new Vector2(0, cornerRadius)) - offset;
                Vector2 inflation = new Vector2((float)Math.Sqrt(u.X * u.X + v.X * v.X), (float)Math.Sqrt(u.Y * u.Y + v.Y * v.Y));

                RectangleF result = ToParentSpace(drawRect).AABBFloat.Inflate(inflation);
                // The above algorithm will return incorrect results if the rounded corners are not fully visible.
                // To limit bad behavior we at least enforce here, that the bounding box with rounded corners
                // is never larger than the bounding box without.
                if (DrawSize.X < CornerRadius * 2 || DrawSize.Y < CornerRadius * 2)
                    result.Intersect(base.BoundingBox);

                return result;
            }
        }

        internal override bool BuildKeyboardInputQueue(List<Drawable> queue)
        {
            if (!base.BuildKeyboardInputQueue(queue))
                return false;

            foreach (T d in AliveInternalChildren)
                d.BuildKeyboardInputQueue(queue);

            return true;
        }

        internal override bool BuildMouseInputQueue(Vector2 screenSpaceMousePos, List<Drawable> queue)
        {
            if (!base.BuildMouseInputQueue(screenSpaceMousePos, queue))
                return false;

            foreach (T d in AliveInternalChildren)
                d.BuildMouseInputQueue(screenSpaceMousePos, queue);

            return true;
        }

        protected override void Dispose(bool isDisposing)
        {
            InternalChildren?.ForEach(c => c.Dispose());

            OnAutoSize = null;

            base.Dispose(isDisposing);
        }

        public void FadeGlowTo(float newAlpha, double duration = 0, EasingTypes easing = EasingTypes.None)
        {
            UpdateTransformsOfType(typeof(TransformGlowAlpha));
            TransformFloatTo(EdgeEffect.Colour.Linear.A, newAlpha, duration, easing, new TransformGlowAlpha());
        }
    }
}
