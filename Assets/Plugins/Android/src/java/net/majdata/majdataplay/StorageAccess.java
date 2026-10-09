package net.majdata.majdataplay;

import android.app.Activity;
import android.content.ContentResolver;
import android.content.Context;
import android.database.Cursor;
import android.net.Uri;
import android.os.Build;
import android.os.Bundle;
import android.provider.DocumentsContract;
import android.system.ErrnoException;
import android.system.OsConstants;

import com.unity3d.player.UnityPlayer;

import java.io.FileNotFoundException;
import java.io.IOException;
import java.io.InputStream;
import java.io.OutputStream;
import java.util.ArrayDeque;
import java.util.Arrays;
import java.util.HashMap;
import java.util.HashSet;
import java.util.List;

/** Framework-only SAF bridge. It neither acquires nor releases URI permissions. */
public final class StorageAccess
{
    public static final int SUCCESS = 0;
    public static final int INVALID_ARGUMENT = 1;
    public static final int NOT_FOUND = 2;
    public static final int DIRECTORY_NOT_FOUND = 3;
    public static final int ACCESS_DENIED = 4;
    public static final int UNSUPPORTED = 5;
    public static final int IO_ERROR = 6;
    public static final int MAX_TRANSFER_SIZE = 32768;

    // Bound mutation snapshots and the API-25 parent-search fallback. Enumeration itself streams.
    private static final int MAX_SCANNED_ENTRIES = 100000;
    private static final int MAX_SEARCH_DIRECTORIES = 1024;
    private static final Object MUTATION_LOCK = new Object();
    private static final String[] PROJECTION = {
        DocumentsContract.Document.COLUMN_DOCUMENT_ID,
        DocumentsContract.Document.COLUMN_DISPLAY_NAME,
        DocumentsContract.Document.COLUMN_MIME_TYPE,
        DocumentsContract.Document.COLUMN_FLAGS,
        DocumentsContract.Document.COLUMN_SIZE,
        DocumentsContract.Document.COLUMN_LAST_MODIFIED
    };

    private StorageAccess()
    {
    }

    /** Result fields form the JNI protocol; successful lookups and cursor EOF may have no entry. */
    public static final class Result
    {
        public final int errorCode;
        public final String errorMessage;
        public Entry entry;
        public CursorHandle cursor;
        public StreamHandle stream;
        public byte[] data;
        public int count;

        private Result(int code, String message)
        {
            errorCode = code;
            errorMessage = message;
        }
    }

    /** Authoritative metadata; presence flags distinguish null values from zero. */
    public static final class Entry
    {
        public final String uri;
        // Identity only: never open this URI in place of the grant-preserving uri.
        public final String resourceId;
        public final String name;
        public final boolean directory;
        public final boolean hasSize;
        public final long size;
        public final boolean hasModified;
        public final long modified;
        public final int flags;
        private final String id;

        private Entry(Cursor cursor, Uri anchor) throws IOException
        {
            id = requiredString(cursor, DocumentsContract.Document.COLUMN_DOCUMENT_ID);
            name = requiredString(cursor, DocumentsContract.Document.COLUMN_DISPLAY_NAME);
            String mimeType = requiredString(cursor, DocumentsContract.Document.COLUMN_MIME_TYPE);
            directory = DocumentsContract.Document.MIME_TYPE_DIR.equals(mimeType);
            uri = documentUri(anchor, id).toString();
            resourceId = DocumentsContract.buildDocumentUri(anchor.getAuthority(), id).toString();
            int flagsIndex = cursor.getColumnIndex(DocumentsContract.Document.COLUMN_FLAGS);
            flags = flagsIndex < 0 || cursor.isNull(flagsIndex) ? 0 : cursor.getInt(flagsIndex);
            int sizeIndex = cursor.getColumnIndex(DocumentsContract.Document.COLUMN_SIZE);
            long reportedSize = sizeIndex < 0 || cursor.isNull(sizeIndex) ? -1 : cursor.getLong(sizeIndex);
            hasSize = !directory && reportedSize >= 0;
            size = hasSize ? reportedSize : 0;
            int modifiedIndex = cursor.getColumnIndex(DocumentsContract.Document.COLUMN_LAST_MODIFIED);
            hasModified = modifiedIndex >= 0 && !cursor.isNull(modifiedIndex);
            modified = hasModified ? cursor.getLong(modifiedIndex) : 0;
        }
    }

    private interface Operation
    {
        Result run() throws Exception;
    }

    private static final class StorageFailure extends IOException
    {
        private static final long serialVersionUID = 1L;
        private final int code;

        private StorageFailure(int code, String message)
        {
            super(message);
            this.code = code;
        }
    }

    private static Result success()
    {
        return new Result(SUCCESS, "");
    }

    private static Result entryResult(Entry entry)
    {
        Result result = success();
        result.entry = entry;
        return result;
    }

    private static Result execute(Operation operation)
    {
        try
        {
            return operation.run();
        }
        catch (Exception exception)
        {
            String message = exception.getMessage();
            return new Result(errorCode(exception), message == null ? exception.getClass().getName() : message);
        }
    }

    private static int errorCode(Exception exception)
    {
        // Access failures take precedence even when a provider wraps them as "not found".
        Throwable cause = exception;
        for (int depth = 0; cause != null && depth < 16; depth++, cause = cause.getCause())
        {
            if (cause instanceof SecurityException)
            {
                return ACCESS_DENIED;
            }
            if (cause instanceof ErrnoException)
            {
                int errno = ((ErrnoException)cause).errno;
                if (errno == OsConstants.EACCES || errno == OsConstants.EPERM)
                {
                    return ACCESS_DENIED;
                }
            }
        }
        cause = exception;
        for (int depth = 0; cause != null && depth < 16; depth++, cause = cause.getCause())
        {
            if (cause instanceof StorageFailure)
            {
                return ((StorageFailure)cause).code;
            }
            if (cause instanceof UnsupportedOperationException)
            {
                return UNSUPPORTED;
            }
        }
        cause = exception;
        for (int depth = 0; cause != null && depth < 16; depth++, cause = cause.getCause())
        {
            if (cause instanceof FileNotFoundException ||
                (cause instanceof ErrnoException && ((ErrnoException)cause).errno == OsConstants.ENOENT))
            {
                return NOT_FOUND;
            }
        }
        // Provider IllegalArgumentException is not an invalid user argument: validation below
        // creates an explicit INVALID_ARGUMENT failure before contacting the provider.
        return IO_ERROR;
    }

    private static Context context() throws IOException
    {
        Activity activity = UnityPlayer.currentActivity;
        if (activity == null)
        {
            throw new IOException("Unity's current activity is unavailable.");
        }
        Context application = activity.getApplicationContext();
        if (application == null)
        {
            throw new IOException("The Android application context is unavailable.");
        }
        return application;
    }

    private static Uri resolveUri(Context context, String location) throws IOException
    {
        if (location == null || location.length() == 0 || location.indexOf((char)0) >= 0)
        {
            throw new StorageFailure(INVALID_ARGUMENT, "A content document or tree URI is required.");
        }
        Uri uri = Uri.parse(location);
        List<String> segments = uri.getPathSegments();
        if (!"content".equals(uri.getScheme()) || uri.getAuthority() == null ||
            uri.getAuthority().length() == 0 || uri.getQuery() != null || uri.getFragment() != null)
        {
            throw new StorageFailure(INVALID_ARGUMENT, "Only canonical content document or tree URIs are supported.");
        }
        if (segments.size() == 2 && "tree".equals(segments.get(0)))
        {
            uri = DocumentsContract.buildDocumentUriUsingTree(uri, DocumentsContract.getTreeDocumentId(uri));
        }
        else if (!((segments.size() == 2 && "document".equals(segments.get(0))) ||
            (segments.size() == 4 && "tree".equals(segments.get(0)) && "document".equals(segments.get(2)))))
        {
            throw new StorageFailure(INVALID_ARGUMENT, "The URI does not identify one SAF document or tree.");
        }
        if (!DocumentsContract.isDocumentUri(context, uri))
        {
            throw new StorageFailure(INVALID_ARGUMENT, "The URI does not belong to a DocumentsProvider.");
        }
        return documentUri(uri, DocumentsContract.getDocumentId(uri));
    }

    private static Uri documentUri(Uri anchor, String id)
    {
        if (DocumentsContract.isTreeUri(anchor))
        {
            return DocumentsContract.buildDocumentUriUsingTree(anchor, id);
        }
        return DocumentsContract.buildDocumentUri(anchor.getAuthority(), id);
    }

    private static Uri returnedUri(Context context, Uri anchor, Uri returned) throws IOException
    {
        if (returned == null)
        {
            throw new IOException("The provider returned no document URI.");
        }
        if (!anchor.getAuthority().equals(returned.getAuthority()) || !DocumentsContract.isDocumentUri(context, returned))
        {
            throw new IOException("The provider returned a document outside the original authority.");
        }
        // Retain the original tree grant even if a provider returns a plain document URI.
        return documentUri(anchor, DocumentsContract.getDocumentId(returned));
    }

    private static void validateName(String name) throws IOException
    {
        if (name == null || name.length() == 0 || ".".equals(name) || "..".equals(name) ||
            name.indexOf('/') >= 0 || name.indexOf((char)92) >= 0 || name.indexOf((char)0) >= 0)
        {
            throw new StorageFailure(INVALID_ARGUMENT, "A single child display name without separators is required.");
        }
    }

    private static String requiredString(Cursor cursor, String column) throws IOException
    {
        int index = cursor.getColumnIndex(column);
        if (index < 0 || cursor.isNull(index))
        {
            throw new IOException("The provider omitted required document metadata: " + column);
        }
        return cursor.getString(index);
    }

    private static void requireComplete(Cursor cursor) throws IOException
    {
        if (cursor == null)
        {
            throw new IOException("The provider returned no cursor (it may be unavailable).");
        }
        Bundle extras = cursor.getExtras();
        if (extras != null && extras.getBoolean(DocumentsContract.EXTRA_LOADING, false))
        {
            throw new IOException("The provider is still loading documents; retry after loading completes.");
        }
        String error = extras == null ? null : extras.getString(DocumentsContract.EXTRA_ERROR);
        if (error != null && error.length() != 0)
        {
            throw new IOException("The provider reported a query failure: " + error);
        }
    }

    private static Entry queryEntry(ContentResolver resolver, Uri uri) throws IOException
    {
        try (Cursor cursor = resolver.query(uri, PROJECTION, null, null, null))
        {
            requireComplete(cursor);
            if (!cursor.moveToFirst())
            {
                requireComplete(cursor);
                return null;
            }
            Entry entry = new Entry(cursor, uri);
            if (!entry.id.equals(DocumentsContract.getDocumentId(uri)) || cursor.moveToNext())
            {
                throw new IOException("The provider returned ambiguous document metadata.");
            }
            requireComplete(cursor);
            return entry;
        }
    }

    private static Entry requireEntry(ContentResolver resolver, Uri uri) throws IOException
    {
        Entry entry = queryEntry(resolver, uri);
        if (entry == null)
        {
            throw new FileNotFoundException("The document does not exist.");
        }
        return entry;
    }

    private static Entry requireDirectory(ContentResolver resolver, Uri uri) throws IOException
    {
        Entry entry;
        try
        {
            entry = queryEntry(resolver, uri);
        }
        catch (Exception exception)
        {
            if (errorCode(exception) == NOT_FOUND)
            {
                throw new StorageFailure(DIRECTORY_NOT_FOUND, "The parent directory does not exist.");
            }
            throw exception;
        }
        if (entry == null)
        {
            throw new StorageFailure(DIRECTORY_NOT_FOUND, "The directory does not exist.");
        }
        if (!entry.directory)
        {
            throw new IOException("The document is not a directory.");
        }
        return entry;
    }

    private static Cursor queryChildren(ContentResolver resolver, Uri directory) throws IOException
    {
        String parentId = DocumentsContract.getDocumentId(directory);
        Uri children = DocumentsContract.isTreeUri(directory)
            ? DocumentsContract.buildChildDocumentsUriUsingTree(directory, parentId)
            : DocumentsContract.buildChildDocumentsUri(directory.getAuthority(), parentId);
        Cursor cursor = resolver.query(children, PROJECTION, null, null, null);
        try
        {
            requireComplete(cursor);
            return cursor;
        }
        catch (Exception exception)
        {
            if (cursor != null)
            {
                cursor.close();
            }
            throw exception;
        }
    }

    private static Entry findChild(ContentResolver resolver, Uri directory, String name) throws IOException
    {
        Entry found = null;
        try (Cursor cursor = queryChildren(resolver, directory))
        {
            while (cursor.moveToNext())
            {
                Entry entry = new Entry(cursor, directory);
                if (name.equals(entry.name))
                {
                    if (found != null)
                    {
                        throw new IOException("The provider returned duplicate child display names.");
                    }
                    found = entry;
                }
            }
            requireComplete(cursor);
        }
        return found;
    }

    private static void requireCapability(Entry entry, int flag, String operation) throws IOException
    {
        if ((entry.flags & flag) == 0)
        {
            throw new StorageFailure(UNSUPPORTED, "The provider does not support " + operation + " for this document.");
        }
    }

    /** Queries metadata; null entry means absence, never a caught permission/provider failure. */
    public static Result getEntry(String location)
    {
        return execute(() -> {
            Context context = context();
            return entryResult(queryEntry(context.getContentResolver(), resolveUri(context, location)));
        });
    }

    /** Resolves a single immediate child using exact, ordinal display-name comparison. */
    public static Result getChildEntry(String location, String name)
    {
        return execute(() -> {
            validateName(name);
            Context context = context();
            ContentResolver resolver = context.getContentResolver();
            Uri directory = resolveUri(context, location);
            requireDirectory(resolver, directory);
            return entryResult(findChild(resolver, directory, name));
        });
    }

    /** Transfers ownership of a cursor to the caller; close even after EOF or an early break. */
    public static Result enumerateEntries(String location)
    {
        return execute(() -> {
            Context context = context();
            ContentResolver resolver = context.getContentResolver();
            Uri directory = resolveUri(context, location);
            requireDirectory(resolver, directory);
            Result result = success();
            result.cursor = new CursorHandle(queryChildren(resolver, directory), directory);
            return result;
        });
    }

    /** Owns one provider cursor; JNI wrappers never retain per-row cursor references. */
    public static final class CursorHandle
    {
        private Cursor cursor;
        private final Uri directory;

        private CursorHandle(Cursor cursor, Uri directory)
        {
            this.cursor = cursor;
            this.directory = directory;
        }

        public synchronized Result next()
        {
            return execute(() -> {
                if (cursor == null)
                {
                    throw new IOException("The document cursor is closed.");
                }
                requireComplete(cursor);
                if (!cursor.moveToNext())
                {
                    requireComplete(cursor);
                    return success();
                }
                return entryResult(new Entry(cursor, directory));
            });
        }

        public synchronized Result close()
        {
            return execute(() -> {
                Cursor owned = cursor;
                cursor = null;
                if (owned != null)
                {
                    owned.close();
                }
                return success();
            });
        }
    }

    /** Opens an existing, nonvirtual binary document. The caller owns the returned stream. */
    public static Result openRead(String location)
    {
        return execute(() -> {
            Context context = context();
            ContentResolver resolver = context.getContentResolver();
            Uri uri = resolveUri(context, location);
            requireBinaryFile(requireEntry(resolver, uri));
            InputStream input = resolver.openInputStream(uri);
            if (input == null)
            {
                throw new IOException("The provider returned no input stream.");
            }
            Result result = success();
            result.stream = new StreamHandle(input, null);
            return result;
        });
    }

    /** Uses explicit wt/wa modes; never falls back to w or emulates append with seeking. */
    public static Result openWrite(String location, boolean append)
    {
        return execute(() -> {
            Context context = context();
            ContentResolver resolver = context.getContentResolver();
            Uri uri = resolveUri(context, location);
            Entry entry = requireEntry(resolver, uri);
            requireBinaryFile(entry);
            requireCapability(entry, DocumentsContract.Document.FLAG_SUPPORTS_WRITE, "writing");
            OutputStream output = resolver.openOutputStream(uri, append ? "wa" : "wt");
            if (output == null)
            {
                throw new IOException("The provider returned no output stream.");
            }
            Result result = success();
            result.stream = new StreamHandle(null, output);
            return result;
        });
    }

    private static void requireBinaryFile(Entry entry) throws IOException
    {
        if (entry.directory)
        {
            throw new IOException("A directory cannot be opened as a binary file.");
        }
        if ((entry.flags & DocumentsContract.Document.FLAG_VIRTUAL_DOCUMENT) != 0)
        {
            throw new StorageFailure(UNSUPPORTED, "Virtual documents have no directly readable binary representation.");
        }
    }

    /** Owns framework streams, not native file descriptors. All transfer buffers are bounded. */
    public static final class StreamHandle
    {
        private InputStream input;
        private OutputStream output;
        private byte[] readBuffer;
        private boolean closed;

        private StreamHandle(InputStream input, OutputStream output)
        {
            this.input = input;
            this.output = output;
        }

        public synchronized Result read(int count)
        {
            return execute(() -> {
                if (closed || input == null)
                {
                    throw new StorageFailure(UNSUPPORTED, "The stream is not readable.");
                }
                if (count < 0 || count > MAX_TRANSFER_SIZE)
                {
                    throw new StorageFailure(INVALID_ARGUMENT, "The read exceeds the bounded transfer size.");
                }
                Result result = success();
                if (count == 0)
                {
                    result.data = new byte[0];
                    return result;
                }
                if (readBuffer == null)
                {
                    readBuffer = new byte[MAX_TRANSFER_SIZE];
                }
                result.count = input.read(readBuffer, 0, count);
                if (result.count < -1 || result.count > count)
                {
                    throw new IOException("The provider returned an invalid read count.");
                }
                if (result.count == 0)
                {
                    throw new IOException("The provider made no progress on a nonempty read.");
                }
                if (result.count > 0)
                {
                    result.data = Arrays.copyOf(readBuffer, result.count);
                }
                return result;
            });
        }

        public synchronized Result write(byte[] data, int count)
        {
            return execute(() -> {
                if (closed || output == null)
                {
                    throw new StorageFailure(UNSUPPORTED, "The stream is not writable.");
                }
                if (data == null || count < 0 || count > MAX_TRANSFER_SIZE || count > data.length || data.length > MAX_TRANSFER_SIZE)
                {
                    throw new StorageFailure(INVALID_ARGUMENT, "The write exceeds the bounded transfer size.");
                }
                output.write(data, 0, count);
                return success();
            });
        }

        public synchronized Result flush()
        {
            return execute(() -> {
                if (closed)
                {
                    throw new IOException("The stream is closed.");
                }
                if (output != null)
                {
                    output.flush();
                }
                return success();
            });
        }

        public synchronized Result close()
        {
            return execute(() -> {
                if (closed)
                {
                    return success();
                }
                closed = true;
                InputStream ownedInput = input;
                OutputStream ownedOutput = output;
                input = null;
                output = null;
                readBuffer = null;
                if (ownedInput != null)
                {
                    ownedInput.close();
                }
                if (ownedOutput != null)
                {
                    ownedOutput.close();
                }
                return success();
            });
        }
    }

    private static final class ChildSnapshot
    {
        private final HashMap<String, Entry> names = new HashMap<>();
        private final HashSet<String> ids = new HashSet<>();

        private ChildSnapshot(ContentResolver resolver, Uri directory) throws IOException
        {
            try (Cursor cursor = queryChildren(resolver, directory))
            {
                while (cursor.moveToNext())
                {
                    if (ids.size() >= MAX_SCANNED_ENTRIES)
                    {
                        throw new IOException("The child snapshot exceeds the safety limit.");
                    }
                    Entry entry = new Entry(cursor, directory);
                    if (names.put(entry.name, entry) != null || !ids.add(entry.id))
                    {
                        throw new IOException("The provider returned ambiguous children.");
                    }
                }
                requireComplete(cursor);
            }
        }
    }

    /** Creates one child without writing to or accepting an existing sibling document. */
    public static Result createFile(String location, String name, String mimeType)
    {
        return create(location, name, mimeType, false);
    }

    /** Returns an existing same-name directory, or creates exactly one directory. */
    public static Result createDirectory(String location, String name)
    {
        return create(location, name, DocumentsContract.Document.MIME_TYPE_DIR, true);
    }

    private static Result create(String location, String name, String mimeType, boolean directory)
    {
        return execute(() -> {
            validateName(name);
            if (mimeType == null || mimeType.length() == 0 || mimeType.indexOf((char)0) >= 0 ||
                (!directory && DocumentsContract.Document.MIME_TYPE_DIR.equals(mimeType)))
            {
                throw new StorageFailure(INVALID_ARGUMENT, "A non-directory file MIME type is required.");
            }
            synchronized (MUTATION_LOCK)
            {
                Context context = context();
                ContentResolver resolver = context.getContentResolver();
                Uri parent = resolveUri(context, location);
                Entry parentEntry = requireDirectory(resolver, parent);
                ChildSnapshot siblings = new ChildSnapshot(resolver, parent);
                Entry existing = siblings.names.get(name);
                if (existing != null)
                {
                    if (directory && existing.directory)
                    {
                        return entryResult(existing);
                    }
                    throw new IOException("A sibling already has the requested name.");
                }
                requireCapability(parentEntry, DocumentsContract.Document.FLAG_DIR_SUPPORTS_CREATE, "child creation");
                Uri createdUri = returnedUri(context, parent, DocumentsContract.createDocument(resolver, parent, mimeType, name));
                if (siblings.ids.contains(DocumentsContract.getDocumentId(createdUri)))
                {
                    throw new IOException("The provider returned an existing sibling instead of creating a document.");
                }
                Entry created = requireEntry(resolver, createdUri);
                if (created.directory != directory || siblings.names.containsKey(created.name))
                {
                    throw new IOException("The created document has an unexpected type or conflicting actual name; it may still exist.");
                }
                verifyNamedChild(resolver, parent, created);
                return entryResult(created);
            }
        });
    }

    private static void verifyNamedChild(ContentResolver resolver, Uri parent, Entry expected) throws IOException
    {
        Entry actual = findChild(resolver, parent, expected.name);
        if (actual == null || !actual.id.equals(expected.id))
        {
            throw new IOException("The provider did not expose the returned document as an unambiguous immediate child.");
        }
    }

    /** Checks sibling collisions before rename and queries the actual returned URI/name. */
    public static Result rename(String location, String name)
    {
        return execute(() -> {
            validateName(name);
            synchronized (MUTATION_LOCK)
            {
                Context context = context();
                ContentResolver resolver = context.getContentResolver();
                Uri uri = resolveUri(context, location);
                Entry entry = requireEntry(resolver, uri);
                Uri parent = findParent(resolver, uri);
                ChildSnapshot siblings = new ChildSnapshot(resolver, parent);
                if (!siblings.ids.contains(entry.id))
                {
                    throw new IOException("The document is no longer an immediate child of its reported parent.");
                }
                Entry occupied = siblings.names.get(name);
                if (occupied != null && !occupied.id.equals(entry.id))
                {
                    throw new IOException("A sibling already has the requested name.");
                }
                if (entry.name.equals(name))
                {
                    return entryResult(entry);
                }
                requireCapability(entry, DocumentsContract.Document.FLAG_SUPPORTS_RENAME, "renaming");
                Uri renamedUri = returnedUri(context, uri, DocumentsContract.renameDocument(resolver, uri, name));
                Entry renamed = requireEntry(resolver, renamedUri);
                Entry collision = siblings.names.get(renamed.name);
                if (renamed.directory != entry.directory ||
                    (collision != null && !collision.id.equals(entry.id)) ||
                    (!renamed.id.equals(entry.id) && siblings.ids.contains(renamed.id)))
                {
                    throw new IOException("The renamed document has an unexpected type, identity, or conflicting actual name.");
                }
                verifyNamedChild(resolver, parent, renamed);
                return entryResult(renamed);
            }
        });
    }

    private static Uri findParent(ContentResolver resolver, Uri uri) throws IOException
    {
        if (!DocumentsContract.isTreeUri(uri))
        {
            throw new StorageFailure(UNSUPPORTED, "Collision-safe rename requires a tree grant with a discoverable parent.");
        }
        String rootId = DocumentsContract.getTreeDocumentId(uri);
        String targetId = DocumentsContract.getDocumentId(uri);
        if (rootId.equals(targetId))
        {
            throw new StorageFailure(UNSUPPORTED, "The selected tree root's parent is outside this tree grant.");
        }
        if (Build.VERSION.SDK_INT >= 26)
        {
            try
            {
                DocumentsContract.Path path = DocumentsContract.findDocumentPath(resolver, uri);
                if (path == null)
                {
                    throw new IOException("The provider returned no canonical document path.");
                }
                List<String> ids = path.getPath();
                if (ids.size() < 2 || !rootId.equals(ids.get(0)) || !targetId.equals(ids.get(ids.size() - 1)))
                {
                    throw new IOException("The provider returned a path outside the authorized tree.");
                }
                Uri parent = documentUri(uri, ids.get(ids.size() - 2));
                requireDirectory(resolver, parent);
                return parent;
            }
            catch (Exception exception)
            {
                if (errorCode(exception) != UNSUPPORTED)
                {
                    throw exception;
                }
            }
        }
        // Opaque document IDs are never split as paths. On API 25/older providers,
        // discover a parent by bounded traversal, rejecting cycles/ambiguous target parents.
        ArrayDeque<Uri> pending = new ArrayDeque<>();
        HashSet<String> visitedDirectories = new HashSet<>();
        pending.add(documentUri(uri, rootId));
        visitedDirectories.add(rootId);
        Uri found = null;
        int scannedEntries = 0;
        while (!pending.isEmpty())
        {
            Uri directory = pending.removeFirst();
            try (Cursor cursor = queryChildren(resolver, directory))
            {
                while (cursor.moveToNext())
                {
                    if (++scannedEntries > MAX_SCANNED_ENTRIES)
                    {
                        throw new StorageFailure(UNSUPPORTED, "Discovering the rename parent exceeds the safety limit.");
                    }
                    Entry child = new Entry(cursor, directory);
                    if (targetId.equals(child.id))
                    {
                        if (found != null)
                        {
                            throw new IOException("The document has ambiguous parents in the selected tree.");
                        }
                        found = directory;
                    }
                    if (child.directory && !targetId.equals(child.id))
                    {
                        if (!visitedDirectories.add(child.id))
                        {
                            throw new IOException("The provider's directory graph has cycles or multiple parents.");
                        }
                        if (visitedDirectories.size() > MAX_SEARCH_DIRECTORIES)
                        {
                            throw new StorageFailure(UNSUPPORTED, "Discovering the rename parent exceeds the directory safety limit.");
                        }
                        pending.add(Uri.parse(child.uri));
                    }
                }
                requireComplete(cursor);
            }
        }
        if (found == null)
        {
            throw new FileNotFoundException("The document is not a child in the selected tree.");
        }
        return found;
    }

    /** Missing files are a no-op; directories are never accepted by this entry point. */
    public static Result deleteFile(String location)
    {
        return execute(() -> {
            synchronized (MUTATION_LOCK)
            {
                Context context = context();
                ContentResolver resolver = context.getContentResolver();
                Uri uri = resolveUri(context, location);
                Entry entry = queryEntry(resolver, uri);
                if (entry == null)
                {
                    return success();
                }
                if (entry.directory)
                {
                    throw new IOException("DeleteFile cannot delete a directory.");
                }
                deleteDocument(resolver, uri, entry);
                return success();
            }
        });
    }

    /** Delegates recursive subtree deletion to the provider, never walks and deletes aliases. */
    public static Result deleteDirectory(String location, boolean recursive)
    {
        return execute(() -> {
            synchronized (MUTATION_LOCK)
            {
                Context context = context();
                ContentResolver resolver = context.getContentResolver();
                Uri uri = resolveUri(context, location);
                Entry entry = requireDirectory(resolver, uri);
                if (!recursive)
                {
                    try (Cursor cursor = queryChildren(resolver, uri))
                    {
                        if (cursor.moveToNext())
                        {
                            throw new IOException("The directory is not empty; recursive deletion was not requested.");
                        }
                        requireComplete(cursor);
                    }
                }
                deleteDocument(resolver, uri, entry);
                return success();
            }
        });
    }

    private static void deleteDocument(ContentResolver resolver, Uri uri, Entry entry) throws IOException
    {
        requireCapability(entry, DocumentsContract.Document.FLAG_SUPPORTS_DELETE, "deletion");
        if (!DocumentsContract.deleteDocument(resolver, uri))
        {
            throw new IOException("The provider did not delete the document.");
        }
    }
}
