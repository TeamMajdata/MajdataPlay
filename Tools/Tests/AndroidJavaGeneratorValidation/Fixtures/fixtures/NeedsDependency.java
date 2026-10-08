package fixtures;

import dependency.ExternalBase;

/** Requires a superclass that is missing from the isolated negative fixture. */
public class NeedsDependency extends ExternalBase {
    /** Creates the dependent type. */
    public NeedsDependency() { }
}
