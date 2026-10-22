package fixtures;
public @interface Marker {
    String value() default "public fake";
    Class<?> type() default Object.class;
}
