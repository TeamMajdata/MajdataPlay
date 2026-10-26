package fixtures;
public interface Parent {
    int CONSTANT = 4;
    Object find();
    default String label() { return "parent"; }
    static int version() { return 1; }
}
