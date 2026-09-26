package net.majdata.majdataplay.oniimai;

import android.content.Context;
import android.view.*;

/** Cabinet layout with the game pattern filling the entire phone display. */
final class CabinetPage extends ViewGroup {
    private final View header,menu;
    CabinetPage(Context context,View header,View menu){super(context);this.header=header;this.menu=menu;setBackgroundColor(GameUi.PAGE);addView(header);addView(menu);
        if(android.os.Build.VERSION.SDK_INT>=29)setForceDarkAllowed(false);
        if(android.os.Build.VERSION.SDK_INT>=30)setOnApplyWindowInsetsListener((v,insets)->{android.graphics.Insets safe=insets.getInsets(WindowInsets.Type.systemBars()|WindowInsets.Type.displayCutout());setPadding(safe.left,safe.top,safe.right,safe.bottom);return insets;});
    }
    @Override protected void onMeasure(int ws,int hs){
        int totalW=MeasureSpec.getSize(ws),totalH=MeasureSpec.getSize(hs),w=totalW-getPaddingLeft()-getPaddingRight(),h=totalH-getPaddingTop()-getPaddingBottom(),margin=GameUi.dp(getContext(),8);
        header.measure(MeasureSpec.makeMeasureSpec(w-2*margin,MeasureSpec.EXACTLY),MeasureSpec.makeMeasureSpec(Math.round(h*.44f),MeasureSpec.AT_MOST));
        int side=Math.min(w-2*margin,Math.max(1,h-header.getMeasuredHeight()-2*margin));
        menu.measure(MeasureSpec.makeMeasureSpec(side,MeasureSpec.EXACTLY),MeasureSpec.makeMeasureSpec(side,MeasureSpec.EXACTLY));setMeasuredDimension(totalW,totalH);
    }
    @Override protected void onLayout(boolean changed,int l,int t,int r,int b){
        int x=getPaddingLeft(),y=getPaddingTop(),w=getWidth()-x-getPaddingRight(),h=getHeight()-y-getPaddingBottom(),margin=GameUi.dp(getContext(),8),head=header.getMeasuredHeight(),side=menu.getMeasuredWidth();
        header.layout(x+margin,y+margin,x+w-margin,y+margin+head);
        int top=Math.min(h-side-margin,Math.max(head+2*margin,Math.round(h*.56f-side/2f)));
        menu.layout(x+(w-side)/2,y+top,x+(w+side)/2,y+top+side);
    }
}
