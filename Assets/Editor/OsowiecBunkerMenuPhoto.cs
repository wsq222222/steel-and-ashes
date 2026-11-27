using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

public static class OsowiecBunkerMenuPhoto
{
    const string Russian = "Assets/Osowiec/HQ/Art/Textures/HeadquartersBackdrop.png";
    const string English = "Assets/Osowiec/HQ/Art/Textures/HeadquartersBackdrop-en.png";

    // Import both native play-mode captures together, or verify the pair already present in the project.
    public static void Capture()
    {
        string[] targets = { Russian, English };
        string[] options = { "-bunker-menu-photo", "-bunker-menu-photo-en" };
        var args = Environment.GetCommandLineArgs();
        byte[][] images = new byte[2][];
        string[] sources = new string[2];
        for (int i=0; i<2; i++)
        {
            int at=Array.IndexOf(args,options[i]);
            if(at>=0 && (at+1>=args.Length || args[at+1].StartsWith("-")))
                throw new ArgumentException("Missing input PNG after "+options[i]);
            sources[i]=Path.GetFullPath(at<0 ? targets[i] : args[at+1]);
            images[i]=File.ReadAllBytes(sources[i]);
            var check=new Texture2D(2,2,TextureFormat.RGBA32,false,false);
            try
            {
                if(!ImageConversion.LoadImage(check,images[i],false)) throw new InvalidOperationException("Unreadable PNG: "+sources[i]);
                OsowiecBunkerMenuCapture.VerifyWallPrint(check);
            }
            finally { Object.DestroyImmediate(check); }
        }
        if(images[0].SequenceEqual(images[1]))throw new InvalidOperationException("Two separately localized photographs are required.");
        string work = Path.Combine("Library","bunker-menu-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(work);
        bool[] wrote = new bool[2];
        try
        {
            for(int i=0; i<2; i++)
            {
                string target=Path.GetFullPath(targets[i]);
                if(string.Equals(sources[i],target,StringComparison.OrdinalIgnoreCase)) continue;
                string stage=Path.Combine(work,i+".png");
                File.WriteAllBytes(stage,images[i]);
                if(File.Exists(target)) File.Replace(stage,target,Path.Combine(work,i+".previous"));
                else File.Move(stage,target);
                wrote[i]=true;
            }
            OsowiecBunkerLocalisation.PrepareEnglishMenuMeta();
            foreach(string target in targets)
            {
                AssetDatabase.ImportAsset(target,ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
                var texture=AssetDatabase.LoadAssetAtPath<Texture2D>(target);
                if(!texture || texture.width!=OsowiecBunkerMenuCapture.Width || texture.height!=OsowiecBunkerMenuCapture.Height)
                    throw new InvalidOperationException("Menu photograph did not import at native resolution: "+target);
            }
        }
        catch
        {
            for(int i=0; i<2; i++)
            {
                if(!wrote[i])continue;
                string previous=Path.Combine(work,i+".previous");
                if(File.Exists(previous))File.Replace(previous,Path.GetFullPath(targets[i]),null);
                else File.Delete(targets[i]);
            }
            throw;
        }
        finally
        {
            foreach(string file in Directory.GetFiles(work))File.Delete(file);
            Directory.Delete(work);
        }
        Debug.Log("Verified and installed Russian and English 3840 x 2160 bunker backgrounds.");
    }
}
