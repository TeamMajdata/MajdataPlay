package net.majdata.majdataplay.oniimai;

import android.content.SharedPreferences;
import android.graphics.Insets;
import android.os.Build;
import android.text.Layout;
import android.view.Gravity;
import android.view.MotionEvent;
import android.view.View;
import android.view.ViewConfiguration;
import android.view.ViewGroup;
import android.view.ViewParent;
import android.view.WindowInsets;
import android.widget.FrameLayout;
import android.widget.TextView;

/** Draggable positioning for one small Activity child, without a touch-intercepting overlay. */
final class FloatingShortcut implements View.OnTouchListener, View.OnAttachStateChangeListener {
    private static final String X_KEY="displayShortcutX", Y_KEY="displayShortcutY";
    private final TextView view;
    private final SharedPreferences prefs;
    private final int slop;
    private final int[] parentLocation=new int[2], rootLocation=new int[2];
    private ViewGroup parent;
    private int insetLeft,insetTop,insetRight,insetBottom;
    private int lastWidth,lastHeight;
    private float minX,minY,maxX,maxY,contentTop;
    private float savedX=1,savedY;
    private boolean saved,dragged,cancelled;
    private int pointer=-1;
    private float downRawX,downRawY,startX,startY,targetX,targetY;
    private boolean positioned;
    private final Runnable settle=this::place;
    private final View.OnLayoutChangeListener layoutListener=(v,l,t,r,b,ol,ot,or,ob)->{
        // Moving our margins changes l/t/r/b; it must not cancel its own drag.
        boolean changed=v==parent?(l!=ol||t!=ot||r!=or||b!=ob):(r-l!=or-ol||b-t!=ob-ot);
        if(changed) {
            if(pointer!=-1)cancelGesture();
            // Reposition after this traversal so a label-width change cannot leave stale margins.
            schedulePlace();
        }
    };

    /** Call once after adding the existing button; its OnClickListener remains unchanged. */
    static void attach(TextView view,SharedPreferences prefs) {
        new FloatingShortcut(view,prefs);
    }

    private FloatingShortcut(TextView view,SharedPreferences prefs) {
        this.view=view;this.prefs=prefs;
        slop=ViewConfiguration.get(view.getContext()).getScaledTouchSlop();
        try {
            saved=prefs.contains(X_KEY)&&prefs.contains(Y_KEY);
            if(saved) {
                savedX=prefs.getFloat(X_KEY,1);savedY=prefs.getFloat(Y_KEY,0);
                saved=Float.isFinite(savedX)&&Float.isFinite(savedY);
                savedX=unit(savedX);savedY=unit(savedY);
            }
        } catch(ClassCastException ignored) { saved=false; }
        if(!saved) { savedX=1;savedY=0; }
        view.setOnTouchListener(this);
        view.addOnAttachStateChangeListener(this);
        if(view.isAttachedToWindow())onViewAttachedToWindow(view);
    }

    private int dp(float value) { return GameUi.dp(view.getContext(),value); }
    private void schedulePlace() { view.removeCallbacks(settle);view.post(settle); }
    private static float unit(float value) { return Math.max(0,Math.min(1,value)); }
    private static float clamp(float value,float min,float max) { return Math.max(min,Math.min(max,value)); }

    @Override public void onViewAttachedToWindow(View attached) {
        if(!(view.getParent() instanceof ViewGroup))return;
        if(parent!=null)parent.removeOnLayoutChangeListener(layoutListener);
        parent=(ViewGroup)view.getParent();
        parent.addOnLayoutChangeListener(layoutListener);
        view.removeOnLayoutChangeListener(layoutListener);
        view.addOnLayoutChangeListener(layoutListener);
        view.setOnApplyWindowInsetsListener((v,insets)->{
            if(readInsets(insets)) {
                if(pointer!=-1)cancelGesture();
                place();
            }
            return insets;
        });
        readInsets(view.getRootWindowInsets());
        view.requestApplyInsets();
        view.removeCallbacks(settle);view.post(settle);
    }

    @Override public void onViewDetachedFromWindow(View detached) {
        cancelGesture();endGesture();
        if(parent!=null)parent.removeOnLayoutChangeListener(layoutListener);
        view.removeOnLayoutChangeListener(layoutListener);
        view.setOnApplyWindowInsetsListener(null);
        view.removeCallbacks(settle);
        parent=null;
    }

    private boolean readInsets(WindowInsets insets) {
        int left=insetLeft,top=insetTop,right=insetRight,bottom=insetBottom;
        if(insets==null)insetLeft=insetTop=insetRight=insetBottom=0;
        else if(Build.VERSION.SDK_INT>=30) {
            Insets safe=insets.getInsets(WindowInsets.Type.systemBars()|WindowInsets.Type.displayCutout()|WindowInsets.Type.ime());
            insetLeft=safe.left;insetTop=safe.top;insetRight=safe.right;insetBottom=safe.bottom;
        } else {
            insetLeft=insets.getSystemWindowInsetLeft();insetTop=insets.getSystemWindowInsetTop();
            insetRight=insets.getSystemWindowInsetRight();insetBottom=insets.getSystemWindowInsetBottom();
            if(Build.VERSION.SDK_INT>=28&&insets.getDisplayCutout()!=null) {
                insetLeft=Math.max(insetLeft,insets.getDisplayCutout().getSafeInsetLeft());
                insetTop=Math.max(insetTop,insets.getDisplayCutout().getSafeInsetTop());
                insetRight=Math.max(insetRight,insets.getDisplayCutout().getSafeInsetRight());
                insetBottom=Math.max(insetBottom,insets.getDisplayCutout().getSafeInsetBottom());
            }
        }
        return left!=insetLeft||top!=insetTop||right!=insetRight||bottom!=insetBottom;
    }

    /** Convert window insets to parent-local bounds, avoiding double-insetting an inset content view. */
    private boolean bounds() {
        if(parent==null||parent.getWidth()<=0||parent.getHeight()<=0)return false;
        View root=view.getRootView();
        parent.getLocationInWindow(parentLocation);root.getLocationInWindow(rootLocation);
        float rootX=rootLocation[0]-parentLocation[0],rootY=rootLocation[1]-parentLocation[1];
        float left=Math.max(parent.getPaddingLeft(),rootX+insetLeft);
        contentTop=Math.max(parent.getPaddingTop(),rootY+insetTop);
        float right=Math.min(parent.getWidth()-parent.getPaddingRight(),rootX+root.getWidth()-insetRight);
        float bottom=Math.min(parent.getHeight()-parent.getPaddingBottom(),rootY+root.getHeight()-insetBottom);
        int gap=dp(12),availableWidth=Math.max(1,Math.round(right-left)-2*gap);
        if(view.getMaxWidth()!=availableWidth)view.setMaxWidth(availableWidth);
        if(view.getWidth()>0)lastWidth=view.getWidth();
        if(view.getHeight()>0)lastHeight=view.getHeight();
        // After a viewport shrink, old margins can temporarily measure WRAP_CONTENT as zero.
        // Keep enough geometry to move back inside the parent so the next layout can recover.
        int width=lastWidth>0?lastWidth:Math.max(view.getMinimumWidth(),Math.round(
                Layout.getDesiredWidth(view.getText(),view.getPaint()))+view.getCompoundPaddingLeft()+view.getCompoundPaddingRight());
        int height=lastHeight>0?lastHeight:Math.max(view.getMinimumHeight(),
                view.getPaint().getFontMetricsInt(null)+view.getCompoundPaddingTop()+view.getCompoundPaddingBottom());
        minX=left+gap;minY=contentTop+gap;
        maxX=Math.max(minX,right-gap-Math.max(1,width));
        maxY=Math.max(minY,bottom-gap-Math.max(1,height));
        return true;
    }

    private void place() {
        if(pointer!=-1&&!cancelled)return;
        if(!bounds())return;
        float x=saved?minX+savedX*(maxX-minX):maxX;
        float y=saved?minY+savedY*(maxY-minY):contentTop+dp(80);
        moveTo(clamp(x,minX,maxX),clamp(y,minY,maxY));
    }

    /** Actual layout moves keep SurfaceView transparent regions in sync on software-rendered Unity. */
    private void moveTo(float x,float y) {
        if(parent==null||!(view.getLayoutParams() instanceof FrameLayout.LayoutParams))return;
        // Layout is asynchronous. Keep the requested position for the next motion and preference save.
        targetX=Math.round(x);targetY=Math.round(y);positioned=true;
        FrameLayout.LayoutParams params=(FrameLayout.LayoutParams)view.getLayoutParams();
        int left=Math.round(targetX)-parent.getPaddingLeft(),top=Math.round(targetY)-parent.getPaddingTop();
        int gravity=Gravity.TOP|Gravity.LEFT;
        if(params.gravity!=gravity||params.leftMargin!=left||params.topMargin!=top
                ||params.rightMargin!=0||params.bottomMargin!=0) {
            params.gravity=gravity;params.leftMargin=left;params.topMargin=top;
            params.rightMargin=0;params.bottomMargin=0;
            view.setLayoutParams(params);
        }
        if(view.getTranslationX()!=0)view.setTranslationX(0);
        if(view.getTranslationY()!=0)view.setTranslationY(0);
    }

    private static float rawX(MotionEvent event,int index) {
        return Build.VERSION.SDK_INT>=29?event.getRawX(index):event.getRawX()+event.getX(index)-event.getX();
    }
    private static float rawY(MotionEvent event,int index) {
        return Build.VERSION.SDK_INT>=29?event.getRawY(index):event.getRawY()+event.getY(index)-event.getY();
    }
    private void allowParent(boolean allow) {
        ViewParent target=view.getParent();if(target!=null)target.requestDisallowInterceptTouchEvent(!allow);
    }
    private void cancelGesture() {
        if(pointer!=-1) {
            if(bounds())moveTo(clamp(startX,minX,maxX),clamp(startY,minY,maxY));
            cancelled=true;
        }
        view.setPressed(false);allowParent(true);
    }
    private void endGesture() { pointer=-1;dragged=false;cancelled=false;view.setPressed(false);allowParent(true); }

    @Override public boolean onTouch(View target,MotionEvent event) {
        int action=event.getActionMasked();
        if(action==MotionEvent.ACTION_DOWN) {
            if(!view.isEnabled()||!bounds())return false;
            if(!positioned)place();
            pointer=event.getPointerId(0);dragged=false;cancelled=false;
            downRawX=rawX(event,0);downRawY=rawY(event,0);startX=view.getLeft();startY=view.getTop();
            moveTo(startX,startY);
            view.setPressed(true);view.drawableHotspotChanged(event.getX(),event.getY());allowParent(false);
            return true;
        }
        if(pointer==-1)return false;
        if(!view.isEnabled()||view.getVisibility()!=View.VISIBLE)cancelGesture();
        if(action==MotionEvent.ACTION_CANCEL) { cancelGesture();endGesture();return true; }
        if(action==MotionEvent.ACTION_POINTER_DOWN||action==MotionEvent.ACTION_POINTER_UP) {
            // Never turn a two-finger sequence or a switched pointer into a click or saved drag.
            cancelGesture();return true;
        }
        if(cancelled) { if(action==MotionEvent.ACTION_UP)endGesture();return true; }
        int index=event.findPointerIndex(pointer);
        if(index<0) { cancelGesture();if(action==MotionEvent.ACTION_UP)endGesture();return true; }
        float dx=rawX(event,index)-downRawX,dy=rawY(event,index)-downRawY;
        if(action==MotionEvent.ACTION_MOVE||action==MotionEvent.ACTION_UP) {
            if(!dragged&&Math.hypot(dx,dy)>slop) { dragged=true;view.setPressed(false); }
            if(dragged&&bounds()) {
                moveTo(clamp(startX+dx,minX,maxX),clamp(startY+dy,minY,maxY));
            }
        }
        if(action==MotionEvent.ACTION_UP) {
            boolean click=!dragged;
            if(dragged&&bounds()) {
                if(maxX>minX)savedX=unit((targetX-minX)/(maxX-minX));
                if(maxY>minY)savedY=unit((targetY-minY)/(maxY-minY));
                saved=true;prefs.edit().putFloat(X_KEY,savedX).putFloat(Y_KEY,savedY).apply();
            }
            endGesture();
            if(click)view.performClick();
        }
        return true;
    }
}
