using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using ZXing;

namespace ScanPro704.Editor
{
    public static class FullscreenVerification
    {
        public static void VerifyPlayerCapture()
        {
            var args=Environment.GetCommandLineArgs();var index=Array.IndexOf(args,"--qa-dir");
            if(index<0||index+1>=args.Length)throw new Exception("--qa-dir is required.");
            var directory=Path.GetFullPath(args[index+1]);
            var capture=JsonUtility.FromJson<PrototypeGame.QrCapture>(File.ReadAllText(Path.Combine(directory,"player-qr-targets.json")));
            var image=new Texture2D(2,2);image.LoadImage(File.ReadAllBytes(Path.Combine(directory,"player-704-output.png")));
            var atlasImage=new Texture2D(2,2);atlasImage.LoadImage(File.ReadAllBytes(Path.Combine(directory,"player-704-atlas.png")));
            if(atlasImage.width!=capture.atlasWidth||atlasImage.height!=capture.atlasHeight)throw new Exception("Captured venue atlas dimensions do not match the QR target map.");
            var passed=new List<string>();
            void Check(bool condition,string text){if(!condition)throw new Exception(text);passed.Add("PASS "+text);}
            try
            {
                var scale=Math.Min(image.width/(double)capture.atlasWidth,image.height/(double)capture.atlasHeight);
                var profile=AssetDatabase.LoadAssetAtPath<OutputProfile>("Assets/ScanPro/Data/704_Output.asset");
                if(profile==null)throw new Exception("The active output profile is missing.");
                var horizontalSlack=image.width-capture.atlasWidth*scale;
                var horizontalOffset=Mathf.Clamp(profile.fullscreenHorizontalOffset,-.5f,.5f);
                var left=horizontalSlack/2+horizontalOffset*horizontalSlack;var top=(image.height-capture.atlasHeight*scale)/2;
                foreach(var target in capture.targets)
                {
                    var r=target.atlasRect;var x0=(int)Math.Floor(left+r.x*scale);var y0=(int)Math.Floor(top+r.y*scale);
                    var width=(int)Math.Ceiling(left+r.xMax*scale)-x0;var height=(int)Math.Ceiling(top+r.yMax*scale)-y0;
                    var screenPixels=new Color32[width*height];
                    for(var y=0;y<height;y++)for(var x=0;x<width;x++)
                        screenPixels[y*width+x]=image.GetPixel(x0+x,image.height-1-y0-y);
                    var blue=Bounds(screenPixels,width,height,p=>p.r<20&&p.g<120&&p.b>200);
                    Check(blue.width>0&&Math.Abs(blue.width-blue.height)<=1,"Actual player screenshot preserves a square QR tile: "+target.code);
                    var qrWidth=Mathf.RoundToInt(r.width);var qrHeight=Mathf.RoundToInt(r.height);
                    var qrX=Mathf.FloorToInt(r.x);var qrY=Mathf.FloorToInt(r.y);
                    var qrBytes=new byte[qrWidth*qrHeight*3];
                    for(var y=0;y<qrHeight;y++)for(var x=0;x<qrWidth;x++)
                    {
                        Color32 pixel=atlasImage.GetPixel(qrX+x,atlasImage.height-1-qrY-y);var p=y*qrWidth+x;
                        qrBytes[p*3]=pixel.r;qrBytes[p*3+1]=pixel.g;qrBytes[p*3+2]=pixel.b;
                    }
                    var decoded=DecodeQrAtAnyQuarterTurn(qrBytes,qrWidth,qrHeight);
                    Check(decoded==target.code,"Independent decoding of full-resolution venue-atlas QR matches live target: "+target.code);
                }
                if(capture.targets.Count==0)throw new Exception("No fullscreen QR targets were captured.");
                File.WriteAllText(Path.Combine(directory,"fullscreen-qr-checks.txt"),string.Join("\n",passed)+$"\nTOTAL: {passed.Count} passed\nPhysical QR area and scanner hardware: NOT TESTED\n");
                Debug.Log("SCANPRO_FULLSCREEN_QR_CHECKS_OK: "+passed.Count);
            }
            finally{UnityEngine.Object.DestroyImmediate(image);UnityEngine.Object.DestroyImmediate(atlasImage);}
        }

        static string DecodeQrAtAnyQuarterTurn(byte[] pixels,int width,int height)
        {
            var reader=new BarcodeReaderGeneric();var current=pixels;var currentWidth=width;var currentHeight=height;
            for(var turn=0;turn<4;turn++)
            {
                var result=reader.Decode(new RGBLuminanceSource(current,currentWidth,currentHeight,RGBLuminanceSource.BitmapFormat.RGB24));
                if(result!=null)return result.Text;
                var rotated=new byte[current.Length];var nextWidth=currentHeight;var nextHeight=currentWidth;
                for(var y=0;y<currentHeight;y++)for(var x=0;x<currentWidth;x++)
                {
                    var sourceIndex=(y*currentWidth+x)*3;var nextX=currentHeight-1-y;var nextY=x;var destination=(nextY*nextWidth+nextX)*3;
                    rotated[destination]=current[sourceIndex];rotated[destination+1]=current[sourceIndex+1];rotated[destination+2]=current[sourceIndex+2];
                }
                current=rotated;currentWidth=nextWidth;currentHeight=nextHeight;
            }
            return null;
        }

        public static void Verify(OutputProfile profile, Action<bool, string> check)
        {
            var diagnostic = new Texture2D(70, 100, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point };
            diagnostic.SetPixels32(Enumerable.Repeat(new Color32(128,128,128,255),7000).ToArray());
            for (var y=20;y<30;y++) for (var x=10;x<20;x++) diagnostic.SetPixel(x,y,Color.green);
            var colors=new[]{Color.red,Color.blue,Color.yellow,Color.magenta};
            var corners=new[]{new Vector2Int(1,1),new Vector2Int(68,1),new Vector2Int(1,98),new Vector2Int(68,98)};
            for(var i=0;i<corners.Length;i++) diagnostic.SetPixel(corners[i].x,corners[i].y,colors[i]);
            diagnostic.Apply();
            var owner=new GameObject("Temporary fullscreen verification");
            var presenter=owner.AddComponent<VenueOutputPresenter>();presenter.enabled=false;
            presenter.presentationShader=Shader.Find("704/Fullscreen Fit");
            try
            {
                foreach(var resolution in new[]{new Vector2Int(1920,1200),new Vector2Int(1024,1920),new Vector2Int(2688,3840)})
                {
                    var target=new RenderTexture(resolution.x,resolution.y,0);target.Create();
                    Texture2D image=null;
                    try
                    {
                        presenter.Present(diagnostic,target);image=Read(target);
                        var pixels=image.GetPixels32();
                        var content=Bounds(pixels,resolution.x,resolution.y,p=>Math.Max(p.r,Math.Max(p.g,p.b))>60);
                        var square=Bounds(pixels,resolution.x,resolution.y,p=>p.r<3&&p.g>250&&p.b<3);
                        // Expected geometry comes from source dimensions and a single physical pixel scale.
                        var scale=Math.Min(resolution.x/70.0,resolution.y/100.0);
                        var left=(resolution.x-70*scale)/2;var bottom=(resolution.y-100*scale)/2;
                        check(Math.Abs(content.x-left)<=1&&Math.Abs(content.y-bottom)<=1&&Math.Abs(content.width-70*scale)<=1&&Math.Abs(content.height-100*scale)<=1&&
                            Math.Abs(square.width-10*scale)<=1&&Math.Abs(square.height-10*scale)<=1&&Math.Abs(square.width-square.height)<=1,
                            $"GPU fullscreen preserves square pixels and centered complete content at {resolution.x}x{resolution.y}");
                        var cornersMatch=true;
                        for(var i=0;i<corners.Length;i++)
                        {
                            var x=(int)Math.Floor(left+(corners[i].x+.5)*scale);var y=(int)Math.Floor(bottom+(corners[i].y+.5)*scale);
                            cornersMatch &= Same(image.GetPixel(x,y),colors[i]);
                        }
                        check(cornersMatch,$"GPU fullscreen retains all four corners and orientation at {resolution.x}x{resolution.y}");
                    }
                    finally {if(image!=null)UnityEngine.Object.DestroyImmediate(image);target.Release();UnityEngine.Object.DestroyImmediate(target);}
                }
            }
            finally {UnityEngine.Object.DestroyImmediate(owner);UnityEngine.Object.DestroyImmediate(diagnostic);}

            VerifyFaces(profile,check);
            var invalid=UnityEngine.Object.Instantiate(profile);invalid.fullscreenDisplay=-1;
            check(!invalid.Validate(out _),"Negative fullscreen monitor indices are rejected before building");
            UnityEngine.Object.DestroyImmediate(invalid);
        }

        static void VerifyFaces(OutputProfile profile,Action<bool,string> check)
        {
            var source=new Texture2D(520,420,TextureFormat.RGBA32,false){filterMode=FilterMode.Point};
            var colors=new[]{Color.red,Color.green,Color.blue,Color.yellow};
            foreach(var face in profile.surfaces)
            {
                var rect=face.sourcePixels;
                for(var y=rect.y;y<rect.yMax;y++)for(var x=rect.x;x<rect.xMax;x++)
                {
                    var quadrant=(x>=rect.x+rect.width/2 ? 1:0)+(y>=rect.y+rect.height/2 ? 2:0);
                    source.SetPixel(x,source.height-1-y,colors[quadrant]);
                }
            }
            source.Apply();var material=new Material(Shader.Find("704/Output Atlas"));
            var atlas=new RenderTexture(2688,3840,0);atlas.Create();Texture2D image=null;
            try
            {
                OutputCompositor.Blit(source,atlas,material);image=Read(atlas);
                foreach(var face in profile.surfaces)
                {
                    var matches=true;var r=face.outputPixels;
                    for(var quadrant=0;quadrant<4;quadrant++)
                    {
                        // CW 90 degrees sends source (u,v) to destination (1-v,u).
                        var u=(quadrant%2==0 ? .25f:.75f);var v=(quadrant<2 ? .25f:.75f);
                        var x=(int)(r.x+(1-v)*r.width);var y=(int)(r.y+u*r.height);
                        matches &= face.rotation==SurfaceRotation.Degrees90 && Same(image.GetPixel(x,image.height-1-y),colors[quadrant]);
                    }
                    check(matches,"Diagnostic GPU atlas preserves fixed position and CW orientation: "+face.name);
                }
            }
            finally
            {
                if(image!=null)UnityEngine.Object.DestroyImmediate(image);
                atlas.Release();UnityEngine.Object.DestroyImmediate(atlas);UnityEngine.Object.DestroyImmediate(material);UnityEngine.Object.DestroyImmediate(source);
            }
        }

        static Texture2D Read(RenderTexture target)
        {
            var previous=RenderTexture.active;
            try
            {
                RenderTexture.active=target;var image=new Texture2D(target.width,target.height,TextureFormat.RGBA32,false);
                image.ReadPixels(new Rect(0,0,target.width,target.height),0,0);image.Apply();return image;
            }
            finally {RenderTexture.active=previous;}
        }
        static RectInt Bounds(Color32[] pixels,int width,int height,Func<Color32,bool> includes)
        {
            var left=width;var bottom=height;var right=-1;var top=-1;
            for(var y=0;y<height;y++)for(var x=0;x<width;x++)if(includes(pixels[y*width+x]))
            {left=Math.Min(left,x);bottom=Math.Min(bottom,y);right=Math.Max(right,x);top=Math.Max(top,y);}
            return right<0 ? new RectInt() : new RectInt(left,bottom,right-left+1,top-bottom+1);
        }
        static bool Same(Color a,Color b)=>Math.Abs(a.r-b.r)<.02&&Math.Abs(a.g-b.g)<.02&&Math.Abs(a.b-b.b)<.02;
    }
}
