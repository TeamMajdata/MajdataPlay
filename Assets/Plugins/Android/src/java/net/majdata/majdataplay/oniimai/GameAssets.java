package net.majdata.majdataplay.oniimai;

import android.content.Context;
import android.graphics.*;
import android.graphics.drawable.BitmapDrawable;
import android.util.SparseArray;
import java.io.InputStream;

/** Original MajdataPlay sprites, losslessly extracted from the source atlas by Tools/Export-OniimaiUI.ps1. */
final class GameAssets {
    private static final SparseArray<Bitmap> sprites=new SparseArray<>();
    private static Typeface font,chineseFont;
    private static Bitmap mascot;
    static Typeface font(Context c) {
        if("zh-Hans".equals(UiText.language())){
            if(chineseFont==null)try{chineseFont=Typeface.createFromAsset(c.getAssets(),"OniimaiUI/AlimamaFangYuanTiVF-Thin-2.ttf");}
            catch(RuntimeException ex){chineseFont=Typeface.create("sans-serif",Typeface.NORMAL);}
            return chineseFont;
        }
        if(font==null)try{font=Typeface.createFromAsset(c.getAssets(),"OniimaiUI/Jua-Regular.ttf");}
        catch(RuntimeException ex){font=Typeface.create("sans-serif",Typeface.NORMAL);}
        return font;
    }
    static Bitmap mascot(Context c) {
        if(mascot==null)try(InputStream in=c.getAssets().open("OniimaiUI/xxlb.png")){mascot=BitmapFactory.decodeStream(in);}catch(Exception ignored){}
        return mascot;
    }
    static Bitmap sprite(Context c,int id) {
        Bitmap cached=sprites.get(id);if(cached!=null)return cached;
        try(InputStream in=c.getAssets().open("OniimaiUI/ui-"+id+".png")) {
            BitmapFactory.Options options=new BitmapFactory.Options();options.inSampleSize=2;
            cached=BitmapFactory.decodeStream(in,null,options);
            if(cached!=null)sprites.put(id,cached);return cached;
        }catch(Exception ex){android.util.Log.e("Oniimai","Unable to load UI sprite "+id,ex);return null;}
    }
    static BitmapDrawable drawable(Context c,int id){return new BitmapDrawable(c.getResources(),sprite(c,id));}
    static void draw(Canvas canvas,Context c,int id,float l,float t,float r,float b,Paint paint){
        Bitmap bitmap=sprite(c,id);if(bitmap!=null)canvas.drawBitmap(bitmap,null,new RectF(l,t,r,b),paint);
    }
}
