package net.majdata.majdataplay.oniimai;
public final class KeyboardStateTest {
    static int checks;
    static void check(boolean value,String message){checks++;if(!value)throw new AssertionError(message);}
    public static void main(String[] args){
        KeyboardState k=new KeyboardState();
        check(k.event(3,51,true,0),"initial press");check(k.mask()==1,"button 1");
        check(!k.event(3,51,true,1),"Android repeat ignored");check(!k.event(3,51,true,0),"duplicate zero repeat ignored");
        check(k.event(3,33,true,0),"simultaneous second press");check(k.mask()==3,"simultaneous bitmask");
        k.event(4,51,true,0);k.event(3,51,false,0);check(k.mask()==3,"other keyboard still holds key");
        k.event(4,51,false,0);check(k.mask()==2,"last release clears key");
        k.event(3,33,false,0);check(k.mask()==0,"all released");
        check(k.event(3,51,true,0),"immediate re-press");
        k.learn(7,51);check(k.map[0]==-1&&k.map[7]==51,"learning removes duplicate map");check(k.mask()==128,"new mapping");
        k.clear();check(k.mask()==0,"focus loss clears held state");
        check(!k.event(3,51,true,3),"repeat after focus loss does not rearm");check(k.mask()==0,"repeat remains unpressed");
        k.event(3,66,true,0);check(k.mask()==0,"unmapped Enter is not a game button");
        k.learn(0,66);check(k.mask()==1,"Enter can be explicitly learned as a sensor button");
        k.clear(); k.event(3,155,true,0); check(k.mask()==256,"P1 separate from ring buttons");
        k.event(3,155,true,1); check(k.mask()==256,"P1 held repeat");
        k.event(3,155,false,0); check(k.mask()==0,"P1 release");
        k.learn(8,66); k.event(3,66,true,0); check(k.mask()==256&&k.map[0]==-1,"P1 learning removes duplicate ring mapping");
        System.out.println("PASS: "+checks+" keyboard-mapping checks");
    }
}
