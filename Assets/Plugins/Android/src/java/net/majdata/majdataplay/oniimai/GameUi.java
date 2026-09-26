package net.majdata.majdataplay.oniimai;

import android.content.Context;
import android.content.res.ColorStateList;
import android.graphics.*;
import android.graphics.drawable.*;
import android.text.TextUtils;
import android.view.*;
import android.widget.*;

/** Shared game-inspired components for the companion screen and in-game settings. */
final class GameUi {
    static final int BLUE=0xff466f97, INK=0xff5c4f49, MUTED=0xff58626c, PAPER=0xffffffff,
            LINE=0xffe4e8ea, PINK=0xffdf5799, GREEN=0xff49a994, ORANGE=0xffe99b45,
            SLATE=0xff4e5663, GOLD=0xffe1cea3, TAB=0xff5e7380, ERROR=0xffb53d49;
    static final int[] JUDGMENT={0xffb45309,0xff8b6900,0xffa93680,0xff427c1b,0xff55545b,0xff286caa,0xffb84650};
    static final int PAGE=0xfff3f5f7, TOUCH_DP=48, RADIUS_DP=24, MOTION_MS=140;
    private GameUi() {}
    static String tr(String ko,String zh) { return "zh-Hans".equals(UiText.language())?zh:ko; }
    static int dp(Context c,float value) { return Math.round(value*c.getResources().getDisplayMetrics().density); }
    static void buttonRole(View view){view.setFocusable(true);view.setAccessibilityDelegate(new View.AccessibilityDelegate(){@Override public void onInitializeAccessibilityNodeInfo(View host,android.view.accessibility.AccessibilityNodeInfo info){super.onInitializeAccessibilityNodeInfo(host,info);info.setClassName("android.widget.Button");}});}
    static void appear(View view){if(!android.animation.ValueAnimator.areAnimatorsEnabled())return;view.animate().cancel();view.setAlpha(.55f);view.setTranslationY(dp(view.getContext(),4));view.animate().alpha(1).translationY(0).setDuration(MOTION_MS).start();}
    static void enabled(View view,boolean enabled){view.setEnabled(enabled);view.setAlpha(enabled?1f:.38f);}
    static void surface(View view){view.setBackground(shape(view.getContext(),PAPER,RADIUS_DP));view.setClipToOutline(true);view.setOutlineProvider(new ViewOutlineProvider(){@Override public void getOutline(View v,Outline outline){outline.setRoundRect(0,0,v.getWidth(),v.getHeight(),dp(v.getContext(),RADIUS_DP));}});}
    static GradientDrawable shape(Context c,int color,float radius) {
        GradientDrawable d=new GradientDrawable(); d.setColor(color); d.setCornerRadius(dp(c,radius)); return d;
    }
    static Drawable ripple(Context c,int color,float radius) {
        return new RippleDrawable(ColorStateList.valueOf(0x22669edb),shape(c,color,radius),shape(c,Color.WHITE,radius));
    }
    static LinearLayout column(Context c) { LinearLayout v=new LinearLayout(c); v.setOrientation(1); if(android.os.Build.VERSION.SDK_INT>=29)v.setForceDarkAllowed(false);return v; }
    static TextView text(Context c,String value,int size,int color,boolean bold) {
        TextView v=new TextView(c); v.setText(value); v.setTextSize(size); v.setTextColor(color);
        v.setTypeface(bold?GameAssets.font(c):Typeface.create("sans-serif",Typeface.NORMAL));
        v.setIncludeFontPadding(false); v.setLineSpacing(dp(c,3),1); return v;
    }
    static void setText(TextView view,String text) { if(view!=null&&!TextUtils.equals(view.getText(),text))view.setText(text); }
    static void space(LinearLayout parent,int height) { View v=new View(parent.getContext()); parent.addView(v,new LinearLayout.LayoutParams(1,dp(parent.getContext(),height))); }
    static LinearLayout card(LinearLayout parent,String title,String subtitle) {
        Context c=parent.getContext(); LinearLayout card=column(c);
        card.setBackground(shape(c,Color.WHITE,22)); card.setPadding(dp(c,18),dp(c,16),dp(c,18),dp(c,16));
        LinearLayout.LayoutParams lp=new LinearLayout.LayoutParams(-1,-2); lp.bottomMargin=dp(c,14); parent.addView(card,lp);
        if(title!=null&&!title.isEmpty()) {
            TextView t=text(c,title,18,INK,true); card.addView(t); space(card,subtitle==null?10:5);
        }
        if(subtitle!=null&&!subtitle.isEmpty()) { card.addView(text(c,subtitle,12,MUTED,false)); space(card,12); }
        return card;
    }
    static TextView action(Context c,String label,boolean primary,Runnable action) {
        TextView b=text(c,label,15,primary?GOLD:INK,true); b.setGravity(Gravity.CENTER);
        b.setMinHeight(dp(c,48)); b.setPadding(dp(c,14),dp(c,12),dp(c,14),dp(c,12));
        b.setBackground(ripple(c,primary?SLATE:0xffe8ecec,28)); b.setClickable(true); b.setFocusable(true);
        buttonRole(b);
        b.setOnClickListener(v->action.run()); return b;
    }
    static TextView row(LinearLayout parent,String title,String value,Runnable action) {
        Context c=parent.getContext(); LinearLayout row=new LinearLayout(c); row.setGravity(Gravity.CENTER_VERTICAL);
        row.setPadding(dp(c,2),dp(c,12),dp(c,2),dp(c,12)); row.setMinimumHeight(dp(c,60));
        LinearLayout words=column(c); TextView label=text(c,title,15,INK,true); words.addView(label);
        TextView detail=text(c,value,12,MUTED,false); detail.setPadding(0,dp(c,5),0,0); words.addView(detail);
        row.addView(words,new LinearLayout.LayoutParams(0,-2,1));
        TextView arrow=text(c,"›",25,BLUE,false); arrow.setGravity(Gravity.CENTER); row.addView(arrow,new LinearLayout.LayoutParams(dp(c,28),dp(c,40)));
        row.setBackground(ripple(c,Color.WHITE,12)); row.setClickable(true); row.setFocusable(true);
        row.setOnClickListener(v->action.run());
        parent.addView(row,new LinearLayout.LayoutParams(-1,-2)); return detail;
    }
    static void divider(LinearLayout parent) { View v=new View(parent.getContext()); v.setBackgroundColor(LINE); parent.addView(v,new LinearLayout.LayoutParams(-1,dp(parent.getContext(),1))); }
    static Switch toggle(LinearLayout parent,String title,String hint,boolean value,android.widget.CompoundButton.OnCheckedChangeListener action) {
        Context c=parent.getContext(); LinearLayout row=new LinearLayout(c); row.setGravity(Gravity.CENTER_VERTICAL); row.setPadding(0,dp(c,12),0,dp(c,12));
        LinearLayout words=column(c); words.addView(text(c,title,15,INK,true));
        if(hint!=null&&!hint.isEmpty()){TextView h=text(c,hint,12,MUTED,false); h.setPadding(0,dp(c,5),dp(c,10),0); words.addView(h);}
        row.addView(words,new LinearLayout.LayoutParams(0,-2,1));
        Switch s=new Switch(new ContextThemeWrapper(c,android.R.style.Theme_Material_Light_NoActionBar)); s.setShowText(false);
        s.setThumbTintList(new ColorStateList(new int[][]{{android.R.attr.state_checked},{}},new int[]{BLUE,0xffa8b5c9}));
        s.setTrackTintList(new ColorStateList(new int[][]{{android.R.attr.state_checked},{}},new int[]{0xffc2dcf6,LINE}));
        s.setMinHeight(dp(c,48)); s.setContentDescription(title); s.setChecked(value); s.setOnCheckedChangeListener(action);
        row.addView(s); row.setOnClickListener(v->s.setChecked(!s.isChecked())); parent.addView(row,new LinearLayout.LayoutParams(-1,-2)); return s;
    }
    static EditText number(LinearLayout parent,String label,int value,int min,int max) {
        Context c=parent.getContext(); TextView t=text(c,label+" · "+min+"–"+max,13,MUTED,true); parent.addView(t); space(parent,7);
        EditText input=new EditText(new ContextThemeWrapper(c,android.R.style.Theme_Material_Light_NoActionBar)); input.setSingleLine(); input.setInputType(2);
        input.setTextColor(INK); input.setTypeface(GameAssets.font(c)); input.setTextSize(18); input.setText(String.valueOf(value)); input.setSelectAllOnFocus(true);
        input.setPadding(dp(c,14),dp(c,12),dp(c,14),dp(c,12));
        StateListDrawable fields=new StateListDrawable();GradientDrawable focus=shape(c,0xfff4f7f9,12);focus.setStroke(dp(c,2),BLUE);GradientDrawable normal=shape(c,0xffedf0f2,12);normal.setStroke(dp(c,1),LINE);fields.addState(new int[]{android.R.attr.state_focused},focus);fields.addState(new int[]{},normal);input.setBackground(fields);input.setContentDescription(label+" · "+min+"–"+max);
        input.setId(View.generateViewId());t.setLabelFor(input.getId());input.setImeOptions(android.view.inputmethod.EditorInfo.IME_ACTION_NEXT);
        input.setMinHeight(dp(c,52));parent.addView(input,new LinearLayout.LayoutParams(-1,-2)); space(parent,16); return input;
    }
    static int readNumber(EditText field,int min,int max) {
        try { int v=Integer.parseInt(field.getText().toString().trim()); if(v<min||v>max)throw new NumberFormatException(); field.setError(null); return v; }
        catch(NumberFormatException e){field.setError(tr("입력 범위: ","输入范围：")+min+"–"+max); field.requestFocus(); throw e;}
    }
    static final class Pattern extends Drawable {
        private final Paint p=new Paint(Paint.ANTI_ALIAS_FLAG);
        @Override public void draw(Canvas c) {
            Rect b=getBounds();c.drawColor(PAPER);float unit=Math.max(50,b.width()/6f);p.setColor(0xfff4f6fa);
            for(int y=-1;y<b.height()/unit+1;y++)for(int x=-1;x<7;x++){
                float cx=(x+.3f+(y%2)*.5f)*unit,cy=(y+.25f)*unit,r=unit*.22f;int kind=Math.floorMod(x+y*3,5);p.setStyle(Paint.Style.FILL);
                if(kind==0)c.drawCircle(cx,cy,r,p);
                else if(kind==1){p.setStyle(Paint.Style.STROKE);p.setStrokeWidth(unit*.055f);c.drawCircle(cx,cy,r,p);}
                else{Path path=new Path();int count=kind==2?10:kind==3?12:6;
                    for(int n=0;n<count;n++){double a=Math.PI*2*n/count-Math.PI/2;float rr=kind==2?(n%2==0?r:r*.58f):kind==3?(n%3==1?r*.22f:r):(n%3==0?r*.18f:r);
                        float px=cx+(float)Math.cos(a)*rr,py=cy+(float)Math.sin(a)*rr;if(n==0)path.moveTo(px,py);else path.lineTo(px,py);}
                    path.close();c.drawPath(path,p);
                }
            }
        }
        @Override public void setAlpha(int a){} @Override public void setColorFilter(ColorFilter f){} @Override public int getOpacity(){return PixelFormat.OPAQUE;}
    }
}
