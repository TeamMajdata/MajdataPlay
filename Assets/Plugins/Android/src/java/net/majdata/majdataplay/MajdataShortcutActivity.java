package net.majdata.majdataplay;

import android.app.Activity;
import android.content.Intent;
import android.os.Bundle;

/**
 * Forwards static shortcuts without clearing the Unity activity's task.
 */
public class MajdataShortcutActivity extends Activity
{
    /**
     * Delivers the shortcut intent to the existing player or starts a new player.
     *
     * @param savedInstanceState The state supplied when Android recreates this activity.
     */
    @Override
    protected void onCreate(Bundle savedInstanceState)
    {
        super.onCreate(savedInstanceState);
        try
        {
            final Intent intent = new Intent(getIntent());
            intent.setClass(this, MajdataPlayActivity.class);
            // Static shortcuts force CLEAR_TASK. Replace their flags before entering the Unity task.
            intent.setFlags(Intent.FLAG_ACTIVITY_NEW_TASK
                    | Intent.FLAG_ACTIVITY_CLEAR_TOP
                    | Intent.FLAG_ACTIVITY_SINGLE_TOP);
            startActivity(intent);
        }
        finally
        {
            // Theme.NoDisplay requires this activity to finish before it resumes.
            finish();
        }
    }
}
