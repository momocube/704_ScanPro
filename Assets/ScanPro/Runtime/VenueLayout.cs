using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace ScanPro704
{
    [CreateAssetMenu(menuName = "704/Venue Layout")]
    public sealed class VenueLayout : ScriptableObject
    {
        public Texture2D background;
        public int pixelsPerCell = 10;
        [Tooltip("尚未由場域資料校正時保持 0；不得由圖片像素推算公分。")]
        public float cellWidthCm;
        public float cellHeightCm;
        public const float MinimumBarcodeAreaCm2 = 20f;
        // Shared by every level; these fixed regions are never editable gameplay areas.
        public static readonly RectInt EntrancePixels = new RectInt(300,0,80,100);
        public static readonly RectInt ScoreboardPixels = new RectInt(380,0,40,100);
        // SafeGreen is only authored on the Floor face. This limits placement, not floor gameplay behavior.
        public static readonly RectInt FloorPixels = new RectInt(100,100,320,220);
        public static readonly RectInt ScoreboardProgressPixels = new RectInt(380,0,40,45);
        public static readonly RectInt ScoreboardCountdownPixels = new RectInt(380,55,40,45);
        bool[] allowedCells;
        Texture2D maskSource;
        int maskCellPixels, maskTextureRevision, maskWidth, maskHeight, maskRevision;
        public int Width { get { EnsureMask(); return maskWidth; } }
        public int Height { get { EnsureMask(); return maskHeight; } }
        public int MaskRevision { get { EnsureMask(); return maskRevision; } }
        public bool HasPhysicalScale => cellWidthCm > 0 && cellHeightCm > 0;

        void OnValidate() => InvalidateMask();
        public void InvalidateMask() { allowedCells = null; }
        void EnsureMask()
        {
            var update = background == null ? -1 : unchecked((int)background.updateCount);
            if (allowedCells != null && maskSource == background && maskCellPixels == pixelsPerCell && maskTextureRevision == update) return;
            maskSource = background; maskCellPixels = pixelsPerCell; maskTextureRevision = update; maskRevision++;
            var unit = Mathf.Max(1, pixelsPerCell);
            maskWidth = background == null ? 52 : background.width / unit;
            maskHeight = background == null ? 42 : background.height / unit;
            allowedCells = new bool[maskWidth * maskHeight];
            if (background == null || !background.isReadable) return;
            var source = background.GetPixels32(); var width = background.width; var height = background.height;
            for (var y = 0; y < maskHeight; y++) for (var x = 0; x < maskWidth; x++)
            {
                var pixel = source[(height - 1 - (y * unit + unit / 2)) * width + x * unit + unit / 2];
                var cellPixels = new RectInt(x*unit,y*unit,unit,unit);
                allowedCells[y * maskWidth + x] = !EntrancePixels.Overlaps(cellPixels) && !ScoreboardPixels.Overlaps(cellPixels) &&
                    pixel.a >= 128 && (pixel.r > 0 || pixel.g > 0 || pixel.b > 0);
            }
        }

        // Layout coordinates use a top-left origin, matching the supplied PNG.
        public bool IsAllowed(Vector2Int cell)
        {
            EnsureMask();
            return cell.x >= 0 && cell.y >= 0 && cell.x < maskWidth && cell.y < maskHeight && allowedCells[cell.y * maskWidth + cell.x];
        }

        public bool IsAllowed(Vector2Int position, Vector2Int size)
        {
            EnsureMask();
            if (size.x < 1 || size.y < 1 || position.x < 0 || position.y < 0 || position.x + size.x > maskWidth || position.y + size.y > maskHeight) return false;
            for (var y = 0; y < size.y; y++)
                for (var x = 0; x < size.x; x++)
                    if (!allowedCells[(position.y + y) * maskWidth + position.x + x]) return false;
            return true;
        }

        public float AreaCm2(Vector2Int size) => HasPhysicalScale ? size.x * size.y * cellWidthCm * cellHeightCm : 0;

        // Imported animations store the union bounds on LevelItem. Validate only the cells
        // occupied by each visible mask frame so empty space between frames is not treated
        // as a real tile footprint.
        public bool FootprintIsAllowed(LevelItem item)
        {
            if (item == null) return false;
            return item.HasAnimation
                ? AnimatedFramesSatisfyAt(item, item.position, IsAllowed)
                : IsAllowed(item.position, item.size);
        }

        public bool PathEndpointsAreAllowed(LevelItem item)
        {
            if (item == null) return false;
            if (!item.HasAnimation)
            {
                if (!IsAllowed(item.position, item.size)) return false;
                if (item.waypoints == null) return true;
                return item.waypoints.All(point => IsAllowed(point, item.size));
            }

            if (!AnimatedFramesSatisfyAt(item, item.position, IsAllowed)) return false;
            return item.waypoints == null || item.waypoints.All(point => AnimatedFramesSatisfyAt(item, point, IsAllowed));
        }

        public bool TouchesEntrance(LevelItem item) => TouchesRegion(item,EntrancePixels,Mathf.Max(1,pixelsPerCell));
        public static bool TouchesEntrance(LevelItem item,int unit) => TouchesRegion(item,EntrancePixels,unit);
        public bool TouchesScoreboard(LevelItem item) => TouchesRegion(item,ScoreboardPixels,Mathf.Max(1,pixelsPerCell));
        public static bool TouchesScoreboard(LevelItem item,int unit) => TouchesRegion(item,ScoreboardPixels,unit);
        public bool TouchesFixedProtectedArea(LevelItem item) => TouchesFixedProtectedArea(item,Mathf.Max(1,pixelsPerCell));
        public static bool TouchesFixedProtectedArea(LevelItem item,int unit) => TouchesEntrance(item,unit) || TouchesScoreboard(item,unit);

        public bool IsOnFloor(Vector2Int position,Vector2Int size) => IsOnFloor(position,size,Mathf.Max(1,pixelsPerCell));
        public static bool IsOnFloor(Vector2Int position,Vector2Int size,int unit)
        {
            unit=Mathf.Max(1,unit);
            if(size.x<1 || size.y<1) return false;
            var left=position.x*unit;var top=position.y*unit;
            return left>=FloorPixels.xMin && top>=FloorPixels.yMin &&
                (position.x+size.x)*unit<=FloorPixels.xMax && (position.y+size.y)*unit<=FloorPixels.yMax;
        }
        public bool PathIsOnFloor(LevelItem item) => PathIsOnFloor(item,Mathf.Max(1,pixelsPerCell));
        public static bool PathIsOnFloor(LevelItem item,int unit)
        {
            if(item==null) return false;
            if(item.HasAnimation)
                return AnimatedPathSatisfies(item,(position,size)=>IsOnFloor(position,size,unit),true);
            if(!IsOnFloor(item.position,item.size,unit)) return false;
            // The floor is convex, so keeping every waypoint footprint inside it also keeps each
            // straight segment (and the closing segment of a loop) inside the same face.
            if(item.waypoints==null) return true;
            foreach(var point in item.waypoints) if(!IsOnFloor(point,item.size,unit)) return false;
            return true;
        }

        static bool TouchesRegion(LevelItem item,RectInt pixels,int unit)
        {
            if(item==null) return false;
            unit=Mathf.Max(1,unit);
            if(item.HasAnimation) return AnimatedMaskTouchesRegion(item,pixels,unit);
            var size=new Vector2(Mathf.Max(1,item.size.x)*unit,Mathf.Max(1,item.size.y)*unit);
            var forbidden=new Rect(pixels.x-size.x,pixels.y-size.y,pixels.width+size.x,pixels.height+size.y);
            var start=(Vector2)item.position*unit;
            if (SegmentEnters(start,start,forbidden)) return true;
            for(var i=0;i<=item.waypoints.Count && item.waypoints.Count>0;i++)
            {
                var end=(Vector2)(i==item.waypoints.Count ? item.position : item.waypoints[i])*unit;
                if(SegmentEnters(start,end,forbidden)) return true;
                start=end;
            }
            return false;
        }

        static bool AnimatedMaskTouchesRegion(LevelItem item,RectInt pixels,int unit)
        {
            if(item.animationFrames==null) return false;
            var expanded=new Rect(pixels.x-unit,pixels.y-unit,pixels.width+unit,pixels.height+unit);
            foreach(var frame in item.animationFrames)
            {
                if(frame==null) continue;
                var mask=frame.Pixels;
                for(var y=0;y<frame.height;y++) for(var x=0;x<frame.width;x++)
                {
                    if(!mask[y*frame.width+x]) continue;
                    var local=(frame.offset+new Vector2Int(x,y))*unit;
                    var start=(Vector2)item.position*unit+local;
                    if(SegmentEnters(start,start,expanded)) return true;
                    if(item.waypoints==null || item.waypoints.Count==0) continue;
                    for(var i=0;i<=item.waypoints.Count;i++)
                    {
                        var anchor=i==item.waypoints.Count ? item.position : item.waypoints[i];
                        var end=(Vector2)anchor*unit+local;
                        if(SegmentEnters(start,end,expanded)) return true;
                        start=end;
                    }
                }
            }
            return false;
        }
        static bool SegmentEnters(Vector2 start,Vector2 end,Rect region)
        {
            float first=0,last=1;
            bool Axis(float point,float delta,float low,float high)
            {
                if(delta==0) return point>low && point<high;
                var a=(low-point)/delta;var b=(high-point)/delta;
                first=Mathf.Max(first,Mathf.Min(a,b));last=Mathf.Min(last,Mathf.Max(a,b));
                return first<last;
            }
            return Axis(start.x,end.x-start.x,region.xMin,region.xMax) && Axis(start.y,end.y-start.y,region.yMin,region.yMax);
        }

        public bool PathIsAllowed(LevelItem item)
        {
            if(item==null) return false;
            if(item.HasAnimation) return AnimatedPathSatisfies(item,IsAllowed,item.CanMove);
            if (!IsAllowed(item.position, item.size)) return false;
            if (!item.CanMove || item.waypoints.Count == 0) return true;
            var points = new List<Vector2Int> { item.position };
            points.AddRange(item.waypoints); points.Add(item.position);
            foreach (var point in points) if (!IsAllowed(point, item.size)) return false;
            for (var i = 0; i < points.Count - 1; i++)
            {
                var start = points[i]; var end = points[i + 1];
                // The footprint changes only when an edge crosses a grid boundary.
                // Check each interval, including the closing segment of the loop.
                var times = new SortedSet<float> { 0, 1 };
                AddCrossings(start.x, end.x, times); AddCrossings(start.y, end.y, times);
                float previous = 0;
                foreach (var time in times)
                {
                    var point = Vector2.Lerp(start, end, (previous + time) / 2);
                    var first = Vector2Int.FloorToInt(point + Vector2.one * .00001f);
                    var last = Vector2Int.CeilToInt(point + item.size - Vector2.one * .00001f) - Vector2Int.one;
                    if (!IsAllowed(first, last - first + Vector2Int.one)) return false;
                    previous = time;
                }
            }
            return true;
        }

        static bool AnimatedPathSatisfies(LevelItem item,Func<Vector2Int,Vector2Int,bool> footprintAllowed,bool followWaypoints)
        {
            if(item==null || footprintAllowed==null) return false;
            var anchors=new List<Vector2> { item.position };
            var waypoints=item.waypoints ?? new List<Vector2Int>();
            if(followWaypoints) anchors.AddRange(waypoints.Select(point=>(Vector2)point));
            if(followWaypoints && waypoints.Count>0) anchors.Add(item.position);

            foreach(var anchor in anchors)
                if(!AnimatedFramesSatisfyAt(item,anchor,footprintAllowed)) return false;
            if(!followWaypoints || waypoints.Count==0) return true;

            for(var segment=0;segment<anchors.Count-1;segment++)
            {
                var start=anchors[segment]; var end=anchors[segment+1];
                var times=new SortedSet<float>{0,1};
                AddCrossings(Mathf.RoundToInt(start.x),Mathf.RoundToInt(end.x),times);
                AddCrossings(Mathf.RoundToInt(start.y),Mathf.RoundToInt(end.y),times);
                var previous=0f;
                foreach(var time in times)
                {
                    var position=Vector2.Lerp(start,end,(previous+time)*.5f);
                    if(!AnimatedFramesSatisfyAt(item,position,footprintAllowed)) return false;
                    previous=time;
                }
            }
            return true;
        }

        static bool AnimatedFramesSatisfyAt(LevelItem item,Vector2 anchor,Func<Vector2Int,Vector2Int,bool> footprintAllowed)
        {
            if(item.animationFrames==null) return true;
            foreach(var frame in item.animationFrames)
            {
                if(frame==null) continue;
                var mask=frame.Pixels;
                for(var y=0;y<frame.height;y++) for(var x=0;x<frame.width;x++)
                {
                    if(!mask[y*frame.width+x]) continue;
                    var cellPosition=anchor+(Vector2)frame.offset+new Vector2(x,y);
                    var first=Vector2Int.FloorToInt(cellPosition+Vector2.one*.00001f);
                    var last=Vector2Int.CeilToInt(cellPosition+Vector2.one-Vector2.one*.00001f)-Vector2Int.one;
                    if(!footprintAllowed(first,last-first+Vector2Int.one)) return false;
                }
            }
            return true;
        }
        static void AddCrossings(int start, int end, SortedSet<float> times)
        {
            if (start == end) return;
            for (var grid = Math.Min(start, end) + 1; grid < Math.Max(start, end); grid++)
                times.Add((grid - start) / (float)(end - start));
        }
    }
}
