using UnityEngine;

public class Music : MonoBehaviour
{
    public AudioSource introSource;
    public AudioSource loopSource;
    
    private void Start()
    {
        introSource.Play();

        double nextStartTime = AudioSettings.dspTime + introSource.clip.length;
        
        loopSource.PlayScheduled(nextStartTime);
    }

    // Update is called once per frame
    void Update()
    {
        
    }
}
