package net.majdata.majdataplay.oniimai;
import java.util.HashSet;
import java.util.Set;

final class KeyboardState {
    final int[] map={51,33,32,31,52,54,29,45,155}; // ring A1-A8, P1 (numpad multiply)
    private final Set<Long> held=new HashSet<>();
    boolean event(int device,int code,boolean down,int repeat){
        long key=((long)device<<32)|(code&0xffffffffL);
        return down ? repeat==0&&held.add(key) : held.remove(key);
    }
    int mask(){
        int result=0;
        for(long key:held)for(int i=0;i<map.length;i++)if(map[i]>=0&&map[i]==(int)key)result|=1<<i;
        return result;
    }
    void learn(int index,int code){for(int i=0;i<map.length;i++)if(map[i]==code)map[i]=-1;map[index]=code;}
    void clear(){held.clear();}
}
