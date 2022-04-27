using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace FMComp
{
    public partial class Form1 : Form
    {
        Graphics g;
        Image imgBuf;
        Font f;
        List<string> NoteNames = new List<string> { "C", "C#", "D", "D#", "E", "F", "F#", "G", "G#", "A", "A#", "B" };

        //song data stuff
        string songname = "song name";
        string author = "author";
        double bpm = 120.0; //at four beats per measure, this / 4 is measures per minute
        bool useFrameCounting = true;
        float fpb = 5; //frames per beat, alternative playback mode
        int npm = 32; //notes per measure; playback speed is bpm/4/npm or just fpb
        bool pal = false; //whether to use 50hz rather than 60hz for export
        bool percdbl = false;
        bool percedit = false;

        //input stuff
        bool clicked = false;
        bool rightclicked = false;
        int mx = 0, my = 0, scrolldelta = 0; //mouse state vars
        int downx = 0, downy = 0;
        int selectedMeasure = 0;
        int selectedInstrument = 0;
        //int clickRegion = -1; //this determines which of the following is used to interpret keypress
        const int CELLSPERROW = 37;
        int selectedNoteCell = -1;
        int vertCells = 1; //this is how many note cells have been selected, vertically
        int selectedCommand = -1; //this is what command cell you're editing
        int selectedMeasureCell = -1; //for editing up top!
        int selectedString = -1; //for strings
        int selectedNumber = -1; //for decimal numbers
        int measureOffset = 0; //for scroll
        int instrumentOffset = 0;
        //bool editing = false;
        int octave = 3;
        int volume = 0;
        int instrument = 1;
        int advance = 2; //how far to progress after entering a note

        class FMNote
        {
            public int note; //this is in "half-steps above B0"
            public int vol;
            public int inst;
        }

        class PSGNote
        {
            public int note;
            public int vol;
        }

        class NoiseNote
        {
            public bool periodic;
            public int rate;
            public int vol;
        }

        class CommandNote
        {
            public bool jump;
            public int jumptargetmeasure;
            public int jumptargetnote;
            public bool instrumentswap;
            public int instrument;
            public bool percvolchange;
            public byte[] percVol;
            public bool detune;
            public byte[] detuneAMTS;
            public byte[] vsAMTS;
            public byte[] vsTIMES;
            public byte[] vsEnabled;
        }

        class PercNote
        {
            public bool bass;
            public bool snare;
            public bool tom;
            public bool cymbal;
            public bool hihat;
        }

        class Instrument
        {
            public string name;
            //these are duplicated between modulator and carrier (in that order!)
            public struct Wave
            {
                public int multi; //0 - F
                public bool ksr; //rate key scale bit
                public int ksl; //2-bit, level key scale
                public bool sustone; //percussive or sustained tone bit
                public bool vibrato;
                public bool ampmod;
                public bool half; //rectified to half wave?
                public int attack; //ADSR are four bits each
                public int decay;
                public int sustain;
                public int release;
            }
            public Wave[] waves = new Wave[2];
            //these are not duplicated
            public int attenuation; //6-bit (0-63)
            public int feedback; //3-bit (0-7)
        }


        class Clipboard
        {
            public enum CBType { NULL, FM, PSG, NOISE, PERC, COM};
            public CBType type = CBType.NULL;
            public List<FMNote> fmc;
            public List<PSGNote> psgc;
            public List<NoiseNote> noisec;
            public List<PercNote> percc;
            public List<CommandNote> comc; //wish i didn't have to keep all these here but this will be maximally readable and safe.
        }


        //data structures
        List<List<int>> songLayout; //last int in the measure list is a bool for percussion
        List<List<List<FMNote>>> fmMeasures; //first dimension is instrument (horizontal), second is pattern, third is vertical
        List<List<List<PSGNote>>> psgMeasures;
        List<List<NoiseNote>> noiseMeasures;
        List<List<CommandNote>> commandMeasures;
        List<List<PercNote>> percussionMeasures;
        List<Instrument> instruments;

        Clipboard clipboard;

        //drawing stuff
        SolidBrush sb = new SolidBrush(Color.White);
        SolidBrush gb = new SolidBrush(Color.DarkSlateGray);
        SolidBrush rb = new SolidBrush(Color.Red);
        SolidBrush highlight = new SolidBrush(Color.FromArgb(60, 255, 255, 255));
        SolidBrush shade = new SolidBrush(Color.FromArgb(120, 0, 0, 0));
        SolidBrush fmbg = new SolidBrush(Color.FromArgb(40, 0, 0));
        SolidBrush psgbg = new SolidBrush(Color.FromArgb(0, 0, 40));
        SolidBrush percbg = new SolidBrush(Color.FromArgb(0, 40, 0));
        Pen lp;// = new Pen(sb);
        Pen gp;// = new Pen(new SolidBrush(Color.DarkSlateGray));
        int hdiv = 200; //this is for pattern editing
        int secHeight = 200;
        double spacer;// = (ClientSize.Width - hdiv) / 14.0;
        double noteHeight;
        int topdiv;// = (ClientSize.Width - hdiv) * 9 / 14 + hdiv;
        int instdiv;// = (int)((ClientSize.Height - secHeight) / 4.0 + secHeight);

        //constants
        const int COMCELL = 9;
        const int PERCBOOL = 14;
        const int PERCCELL = 15;

        const int MODULATOR = 0;
        const int CARRIER = 1;
        const int WAVOFF = 166;

        const int ROMOFFSET = 0xB69; //this unfortunately needs to be updated each time the driver is updated

        public Form1()
        {
            InitializeComponent();
            ClientSize = new Size(1280, 720);
            DoubleBuffered = true;
            FormBorderStyle = FormBorderStyle.FixedSingle;
            Text = "FM Composer testing ver. B";
            imgBuf = new Bitmap(ClientSize.Width, ClientSize.Height);
            g = Graphics.FromImage(imgBuf);
            f = DefaultFont;
            Cursor = Cursors.Hand;
            MaximizeBox = false;
            //MouseClick += clickhandler;
            MouseDown += clickhandler;
            MouseUp += draghandler;
            MouseMove += movehandler;
            MouseDoubleClick += clickhandler;
            MouseWheel += wheelhandler;
            KeyDown += keyhandler;

            LostFocus += focuslost;

            reset();

            lp = new Pen(sb);
            gp = new Pen(gb);
            spacer = (ClientSize.Width - hdiv) / 14.0;
            topdiv = (ClientSize.Width - hdiv) * 9 / 14 + hdiv;
            instdiv = (int)((ClientSize.Height - secHeight) / 4.0 + secHeight);
            noteHeight = (ClientSize.Height - secHeight) / 32.0;

        }

        public void reset()
        {
            //setup structures
            songLayout = new List<List<int>>();
            for (int i = 0; i < 4; i++)
            {
                songLayout.Add(new List<int> { 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 });
            }
            measureOffset = 0;
            selectedMeasure = 0;
            selectedMeasureCell = -1;
            selectedString = -1;
            songname = "song name";
            author = "author";

            fmMeasures = new List<List<List<FMNote>>>();
            for (int i = 0; i < 9; i++)
            {
                fmMeasures.Add(new List<List<FMNote>>());
                addFMMeasure(i);
            }
            psgMeasures = new List<List<List<PSGNote>>>();
            for (int i = 0; i < 3; i++)
            {
                psgMeasures.Add(new List<List<PSGNote>>());
                addPSGMeasure(i);
            }
            noiseMeasures = new List<List<NoiseNote>>();
            addNoiseMeasure();

            commandMeasures = new List<List<CommandNote>>();
            addCommandMeasure();

            percussionMeasures = new List<List<PercNote>>();
            addPercussionMeasure();

            instruments = new List<Instrument>();// { new Instrument { name = "test instrument" }, new Instrument { name = "second one" } };
            addInstrument();

            clipboard = new Clipboard(); //wipe it here
        }

        public void export(string path, bool player)
        {
            //generate hex
            List<byte> exdata = new List<byte>();
            bool usesPSG = false;
            bool usesFM = false;
            //first, add the infotags
            exdata.Add(0x00); //the chipflags, populated at the end
            char[] sn = songname.ToUpper().PadRight(32).ToCharArray();
            for(int i = 0; i < 32; i++)
                exdata.Add((byte)sn[i]);
            char[] an = author.ToUpper().PadRight(16).ToCharArray();
            for (int i = 0; i < 16; i++)
                exdata.Add((byte)an[i]);
            //then the instrument data
            exdata.Add((byte)instruments.Count);
            for(int i = 0; i < instruments.Count; i++)
            {
                //eight bytes per instrument!
                Instrument ins = instruments[i];
                for (int w = 0; w < 2; w++)
                    exdata.Add((byte)((ins.waves[w].ampmod?0x80:0x00) | (ins.waves[w].vibrato?0x40:0x00) | (ins.waves[w].sustone?0x20:0x00) | (ins.waves[w].ksr?0x10:0x00) | ins.waves[w].multi));
                exdata.Add((byte)((ins.waves[0].ksl << 6) | ins.attenuation));
                exdata.Add((byte)((ins.waves[1].ksl<<6) | (ins.waves[CARRIER].half?0x10:0x00) | (ins.waves[MODULATOR].half?0x08:0x00) | ins.feedback));
                exdata.Add((byte)((ins.waves[0].attack << 4) | ins.waves[0].decay));
                exdata.Add((byte)((ins.waves[1].attack << 4) | ins.waves[1].decay));
                exdata.Add((byte)((ins.waves[0].sustain << 4) | ins.waves[0].release));
                exdata.Add((byte)((ins.waves[1].sustain << 4) | ins.waves[1].release));
            }
            double notelength = fpb;
            if (!useFrameCounting)
                notelength = 1/(bpm*npm/60/4/(pal?50:60));
            double notetimer = 0;
            bool eperc = false;
            List<int> mposes = new List<int>(); //holds measure addresses, for the jump command
            List<int> jumptargets = new List<int>();
            List<int> jumpposes = new List<int>();
            int lastop = 0; //bytes since last op, for delay purposes
            bool longwait = false;
            int trackstart = exdata.Count;
            byte[] detune = new byte[] { 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0}; //nine FM, one PSG

            for(int emeasure = 0; emeasure < songLayout.Count; emeasure++)
            {
                //for each measure, map each note row. but first!
                //percussion mode check
                if (songLayout[emeasure][PERCBOOL] == 1 && !eperc)
                {
                    exdata.Add(0x80); //enable perc
                    eperc = true;
                    lastop = 0;
                    longwait = false;
                }
                else if (songLayout[emeasure][PERCBOOL] == 0 && eperc)
                {
                    exdata.Add(0xB0); //disable perc
                    eperc = false;
                    lastop = 0;
                    longwait = false;
                }
                //now take note of position
                mposes.Add(exdata.Count - trackstart);
                //now go through the note rows
                for (int erow = 0; erow < npm; erow++)
                {
                    //start with commands!
                    {
                        CommandNote note = commandMeasures[songLayout[emeasure][COMCELL]][erow];
                        //handle jump at the end?
                        if(note.percvolchange)
                        {
                            exdata.Add(0xA0);
                            exdata.Add(note.percVol[0]);
                            exdata.Add((byte)((note.percVol[4] << 4) | note.percVol[1]));
                            exdata.Add((byte)((note.percVol[2] << 4) | note.percVol[3]));
                            lastop = 3;
                            longwait = false;
                        }
                        if(note.instrumentswap)
                        {
                            exdata.Add(0x70);
                            exdata.Add((byte)note.instrument);
                            lastop = 1;
                            longwait = false;
                        }
                        if(note.detune)
                        {
                            detune = note.detuneAMTS;
                        }
                        for (int v = 0; v < note.vsEnabled.Length; v++)
                            if (note.vsEnabled[v] != 0 && note.vsTIMES[v] != 0)
                            {
                                //write the comm
                                exdata.Add(0xD0);
                                exdata.Add((byte)v);
                                byte time = (byte)(note.vsTIMES[v] * (note.vsEnabled[v] == 1?1:notelength)); //don't scale for vibrato!
                                exdata.Add(time);
                                byte amount = (byte)((note.vsAMTS[v] - (note.vsEnabled[v] == 2?31:0)) * (note.vsEnabled[v] == 2?2:1) * ((v > 8 && note.vsEnabled[v] == 2)?-1:1) / (note.vsEnabled[v] == 1?1:time)); //don't scale for vibrato, negate for psg slide
                                exdata.Add((byte)((amount & 0x7F) | ((2 - note.vsEnabled[v])<<7))); //vibrato flag bit
                                lastop = 3;
                                longwait = false;
                            }
                    }
                    //now the notes, starting with FM
                    for (int n = 0; n < (songLayout[emeasure][PERCBOOL] == 1 && !percedit ? 6 : 9); n++)
                    {
                        FMNote note = fmMeasures[n][songLayout[emeasure][n]][erow];
                        if (note.vol != -1)
                        {
                            exdata.Add(0x20);
                            exdata.Add((byte)n);//track
                            exdata.Add((byte)((note.inst << 4) | note.vol));//instrument+vol
                            //calc the fnum
                            int sem = (note.note & 0x7F); //mask off note off and sustain
                            double freq = (int)(27.5 * Math.Pow(1.059463094359, sem+2));
                            int octave = (sem + 9) / 12;
                            int fnum = (int)(freq * 32768 / (3125 * Math.Pow(2, octave)) + detune[n]);
                            if (fnum > 511)
                                fnum = 511;
                            else if (fnum < 0)
                                fnum = 0;
                            exdata.Add((byte)(fnum & 0xFF));//note (low freq)
                            exdata.Add((byte)(fnum >> 8 | octave << 1 | ((note.note & 0x100) == 0x100 ? 0x00 : 0x10) | ((note.note & 0x200) == 0x200 ? 0x20 : 0x00)));//note high bit, octave, key on, sustain
                            lastop = 4;
                            longwait = false;
                            usesFM = true;
                        }
                    }
                    //psg
                    for (int n = 0; n < 3; n++)
                    {
                        PSGNote note = psgMeasures[n][songLayout[emeasure][n + 10]][erow];
                        if (note.vol != -1)
                        {
                            if (note.note != 0)
                            {
                                exdata.Add(0x00);
                                double freq = (int)(27.5 * Math.Pow(1.059463094359, note.note + 2));
                                int fnum = (int)(3579545 / (freq * 32) + detune[n + 9]);
                                if (fnum > 1023)
                                    fnum = 1023;
                                else if(fnum < 0)
                                    fnum = 0;
                                exdata.Add((byte)(0x90 | (n << 5) | (note.vol & 0xF))); //volume
                                exdata.Add((byte)(0x80 | (n << 5) | (fnum & 0x0F))); //latch, channel, note
                                exdata.Add((byte)((fnum >> 4) & 0x3F)); //note high bits
                                lastop = 3;
                                longwait = false;
                                usesPSG = true;
                            }
                            else
                            {
                                exdata.Add(0x40);
                                exdata.Add((byte)(0x90 | (n << 5) | (note.vol & 0xF))); //volume
                                lastop = 1;
                                longwait = false;
                            }
                        }
                    }
                    //noise
                    {
                        NoiseNote note = noiseMeasures[songLayout[emeasure][13]][erow];
                        if (note.vol != -1)
                        {
                            exdata.Add(0x10);
                            exdata.Add((byte)(0x80 | (3 << 5) | ((note.periodic ? 0 : 1) << 2) | note.rate));
                            exdata.Add((byte)(0x90 | (3 << 5) | note.vol));
                            lastop = 2;
                            longwait = false;
                            usesPSG = true;
                        }
                    }
                    //percussion
                    if (songLayout[emeasure][PERCBOOL] == 1)
                    {
                        PercNote note = percussionMeasures[songLayout[emeasure][PERCCELL]][erow];
                        //if (note.bass || note.snare || note.tom || note.cymbal || note.hihat)
                        {
                            exdata.Add(0x90);
                            exdata.Add((byte)((note.bass ? 0x10 : 0x00) | (note.snare ? 0x08 : 0x00) | (note.tom ? 0x04 : 0x00) | (note.cymbal ? 0x02 : 0x00) | (note.hihat ? 0x01 : 0x00)));
                            //exdata.Add(0x90);
                            //exdata.Add(0x00);
                            lastop = 1;
                            longwait = false;
                            usesFM = true;
                        }
                    }   

                    //finished the row, now calculate delay
                    int delay;
                    if (longwait)
                        delay = exdata[exdata.Count - 1];
                    else
                        delay = (exdata[exdata.Count - (1 + lastop)] & 0x0F);
                    if (percdbl && songLayout[emeasure][PERCBOOL] == 1)
                    {
                        while (notetimer < notelength/2)
                        {
                            notetimer++;
                            delay++;
                        }
                        notetimer -= notelength/2;
                        //update the note's delay
                        if (longwait)
                            exdata[exdata.Count - 1] = (byte)delay;
                        else
                        {
                            if (delay > 0x0F)
                            {
                                exdata[exdata.Count - (1 + lastop)] &= 0xF0;
                                exdata.Add(0xC0);
                                exdata.Add((byte)delay);
                                longwait = true;
                            }
                            else
                                exdata[exdata.Count - (1 + lastop)] = (byte)((exdata[exdata.Count - (1 + lastop)] & 0xF0) | delay);
                        }
                        //add percussion off
                        exdata.Add(0x90);
                        exdata.Add(0x00);
                        lastop = 1;
                        longwait = false;
                        //second wait
                        delay = 0;
                        while (notetimer < notelength / 2)
                        {
                            notetimer++;
                            delay++;
                        }
                        notetimer -= notelength / 2;
                        //update the note's delay
                        if (longwait)
                            exdata[exdata.Count - 1] = (byte)delay;
                        else
                        {
                            if (delay > 0x0F)
                            {
                                exdata[exdata.Count - (1 + lastop)] &= 0xF0;
                                exdata.Add(0xC0);
                                exdata.Add((byte)delay);
                                longwait = true;
                            }
                            else
                                exdata[exdata.Count - (1 + lastop)] = (byte)((exdata[exdata.Count - (1 + lastop)] & 0xF0) | delay);
                        }
                    }
                    else
                    {
                        while (notetimer < notelength)
                        {
                            notetimer++;
                            delay++;
                        }
                        notetimer -= notelength;
                        //update the note's delay
                        if (longwait)
                        {
                            if (delay > 255)
                            {
                                exdata[exdata.Count - 1] = 255;
                                //add another wait here
                                exdata.Add(0xC0);
                                exdata.Add((byte)(delay - 255)); //start the new wait with the overflow
                            }
                            else
                                exdata[exdata.Count - 1] = (byte)delay;

                        }
                        else
                        {
                            if (delay > 0x0F)
                            {
                                exdata[exdata.Count - (1 + lastop)] &= 0xF0;
                                exdata.Add(0xC0);
                                exdata.Add((byte)delay);
                                longwait = true;
                            }
                            else
                                exdata[exdata.Count - (1 + lastop)] = (byte)((exdata[exdata.Count - (1 + lastop)] & 0xF0) | delay);
                        }
                    }

                    //row delay was calculated, now check for jump note
                    {
                        CommandNote note = commandMeasures[songLayout[emeasure][COMCELL]][erow];
                        if (note.jump)
                        {
                            //check for percussion toggle first!
                            if (songLayout[emeasure][PERCBOOL] == 0 && songLayout[note.jumptargetmeasure][PERCBOOL] == 1)
                                exdata.Add(0x80); //enable perc
                            else if (songLayout[emeasure][PERCBOOL] == 1 && songLayout[note.jumptargetmeasure][PERCBOOL] == 0)
                                exdata.Add(0xB0); //disable perc


                            exdata.Add(0x50);
                            jumpposes.Add(exdata.Count);
                            exdata.Add(0x00);
                            exdata.Add(0x00); //to be filled in at the end
                            jumptargets.Add(note.jumptargetmeasure);
                            lastop = 2;
                            longwait = false;
                        }
                    }
                }
            }
            //done with all the measures, now populate the jumps
            for(int j = 0; j < jumpposes.Count; j++)
            {
                exdata[jumpposes[j]] = (byte)mposes[jumptargets[j]];
                exdata[jumpposes[j]+1] = (byte)(mposes[jumptargets[j]]>>8);
            }

            //and set the chipflags
            exdata[0] = (byte)((usesFM?1:0)|(usesPSG?2:0));

            //and now the song data is generated. let's load up the rom
            byte[] rom;
            if (player)
            {
                var assembly = System.Reflection.Assembly.GetExecutingAssembly();
                var resourcename = "FMComp.output.sms";
                using (Stream fs = assembly.GetManifestResourceStream(resourcename))
                using (BinaryReader br = new BinaryReader(fs))
                {
                    rom = new byte[br.BaseStream.Length];
                    for (int p = 0; p < br.BaseStream.Length; p++)
                        rom[p] = br.ReadByte();
                }
                //and now insert the song into the rom!
                for (int p = 0; p < exdata.Count; p++)
                    rom[ROMOFFSET + p] = exdata[p]; //need to update this value every time the player rom is updated!!!
            }
            else
                rom = exdata.ToArray();
            //and save!
            File.WriteAllBytes(path, rom);
        }

        public void mainLoop()
        {
            //testing stuff

            while (Created)
            {
                //based on mode (edit, playback)
                //check for inputs
                doFrame();


                //render
                draw();

                Application.DoEvents();
            }
        }

        void doFrame()
        {
            //get mouse click?
            if(clicked)
            {
                //get position, figure out where we clicked
                selectedMeasureCell = -1;
                selectedString = -1;
                selectedNumber = -1;
                if (mx < hdiv)
                {
                    if (my < secHeight - 15 || my > secHeight)
                        selectedNoteCell = -1;
                    selectedCommand = -1;
                    if (my > instdiv)
                    {
                        //Debug.WriteLine("clicked inst editor");
                        if(my < instdiv + 16)
                        {
                            if (mx < hdiv / 2)
                                addInstrument();
                            else if(instruments.Count > 1)
                            {
                                instruments.RemoveAt(selectedInstrument);
                                selectedInstrument--;
                                if (selectedInstrument == -1)
                                    selectedInstrument = 0;
                            }
                        }
                        if (my > instdiv + 16 && my < instdiv + 32)
                            selectedString = 2; //instrument name
                        //attenuation & feedback start at selectednumber 3
                        else if (my > instdiv + 32 && my < instdiv + 48)
                        {
                            if (mx < hdiv / 2)
                                selectedNumber = 3;
                            else
                                selectedNumber = 4;
                        }
                        else if (my > instdiv + 48)
                        {
                            int w = 0;
                            if (my >= WAVOFF + 48 + instdiv)
                                w = 1;
                            Instrument.Wave wav = instruments[selectedInstrument].waves[w];
                            my -= instdiv + 48 + w*WAVOFF;
                            if(my > 16 && my < 32)
                            {
                                //adsr vals
                                selectedNumber = 5 + w * 6 + mx / (hdiv / 4);
                            }
                            else if (my > 32 && my < 48)
                            {
                                //multi and ksl vals
                                selectedNumber = 9 + w * 6 + (mx < hdiv / 2 ? 0 : 1);
                            }
                            else if (my > 48 && my < 64)
                            {
                                //sustone, ksr
                                if (mx < hdiv / 2)
                                    wav.sustone = !wav.sustone;
                                else
                                    wav.ksr = !wav.ksr;
                            }
                            else if (my > 64 && my < 80)
                            {
                                //vibrato, ampmod, half
                                if (mx < hdiv / 2)
                                    wav.vibrato = !wav.vibrato;
                                else if (mx < hdiv * 3 / 4)
                                    wav.ampmod = !wav.ampmod;
                                else
                                    wav.half = !wav.half;
                            }
                            instruments[selectedInstrument].waves[w] = wav;
                        }
                    }
                    else
                    {
                        if (my < secHeight)
                        {
                            if (my < 22)
                            {
                                switch (Math.Floor((double)mx / (hdiv / 4)))
                                {
                                    case 0:
                                        Debug.WriteLine("new");
                                        reset();
                                        break;
                                    case 1:
                                        using (OpenFileDialog openFileDialog = new OpenFileDialog())
                                        {
                                            openFileDialog.Filter = "FM project (*.fmp)|*.fmp";
                                            openFileDialog.RestoreDirectory = true;

                                            if (openFileDialog.ShowDialog() == DialogResult.OK)
                                            {

                                                using (BinaryReader br = new BinaryReader(openFileDialog.OpenFile()))
                                                {
                                                    //start with file version byte
                                                    byte ver = br.ReadByte();
                                                    if (ver == 1 || ver == 2 || ver == 3) //version 3 is latest
                                                    {
                                                        //reset arrays for population
                                                        songLayout = new List<List<int>>();
                                                        fmMeasures = new List<List<List<FMNote>>>();
                                                        psgMeasures = new List<List<List<PSGNote>>>();
                                                        noiseMeasures = new List<List<NoiseNote>>();
                                                        commandMeasures = new List<List<CommandNote>>();
                                                        percussionMeasures = new List<List<PercNote>>();
                                                        instruments = new List<Instrument>();
                                                        //now, read the data!
                                                        songname = br.ReadString();//name
                                                        author = br.ReadString();//author
                                                        bpm = br.ReadDouble();//bpm
                                                        fpb = br.ReadSingle();//fpm
                                                        npm = br.ReadInt32();//npm
                                                        byte temp = br.ReadByte();
                                                        useFrameCounting = (temp & 1) == 1;
                                                        pal = (temp & 2) == 2;
                                                        percdbl = (temp & 4) == 4;
                                                        percedit = (temp & 8) == 8;
                                                        //song layout
                                                        int limit = br.ReadInt32();
                                                        for (int m = 0; m < limit; m++)
                                                        {
                                                            List<int> measure = new List<int>();
                                                            for (int c = 0; c < 16; c++)
                                                                measure.Add(br.ReadInt32());
                                                            songLayout.Add(measure);
                                                        }
                                                        //fm
                                                        for (int c = 0; c < 9; c++)
                                                        {
                                                            fmMeasures.Add(new List<List<FMNote>>());
                                                            limit = br.ReadInt32();
                                                            for (int i = 0; i < limit; i++)
                                                            {
                                                                List<FMNote> measure = new List<FMNote>();
                                                                for (int r = 0; r < 32; r++)
                                                                {
                                                                    measure.Add(new FMNote { note = br.ReadInt32(), vol = br.ReadInt32(), inst = br.ReadInt32() });
                                                                    if (measure[measure.Count - 1].note < 0)
                                                                        measure[measure.Count - 1].note = 0; //clean broken notes from earlier builds
                                                                }
                                                                fmMeasures[c].Add(measure);
                                                            }
                                                        }
                                                        //psg
                                                        for (int c = 0; c < 3; c++)
                                                        {
                                                            psgMeasures.Add(new List<List<PSGNote>>());
                                                            limit = br.ReadInt32();
                                                            for (int i = 0; i < limit; i++)
                                                            {
                                                                List<PSGNote> measure = new List<PSGNote>();
                                                                for (int r = 0; r < 32; r++)
                                                                {
                                                                    measure.Add(new PSGNote { note = br.ReadInt32(), vol = br.ReadInt32() });
                                                                }
                                                                psgMeasures[c].Add(measure);
                                                            }
                                                        }
                                                        //noise
                                                        limit = br.ReadInt32();
                                                        for (int i = 0; i < limit; i++)
                                                        {
                                                            List<NoiseNote> measure = new List<NoiseNote>();
                                                            for (int r = 0; r < 32; r++)
                                                            {
                                                                measure.Add(new NoiseNote { periodic = br.ReadBoolean(), vol = br.ReadInt32(), rate = br.ReadInt32() });
                                                            }
                                                            noiseMeasures.Add(measure);
                                                        }
                                                        //command
                                                        limit = br.ReadInt32();
                                                        for (int i = 0; i < limit; i++)
                                                        {
                                                            List<CommandNote> measure = new List<CommandNote>();
                                                            for (int r = 0; r < 32; r++)
                                                            {
                                                                measure.Add(new CommandNote
                                                                {
                                                                    jump = br.ReadBoolean(),
                                                                    jumptargetmeasure = br.ReadInt32(),
                                                                    jumptargetnote = br.ReadInt32(),
                                                                    instrumentswap = br.ReadBoolean(),
                                                                    instrument = br.ReadInt32(),
                                                                    percvolchange = br.ReadBoolean(),
                                                                    percVol = new byte[] {
                                                                        br.ReadByte(),
                                                                        br.ReadByte(),
                                                                        br.ReadByte(),
                                                                        br.ReadByte(),
                                                                        br.ReadByte()
                                                                    },
                                                                    detune = (ver > 1 ? br.ReadBoolean() : false),
                                                                    detuneAMTS = (ver > 1 ? new byte[]
                                                                    {
                                                                        br.ReadByte(), br.ReadByte(), br.ReadByte(), br.ReadByte(),
                                                                        br.ReadByte(), br.ReadByte(), br.ReadByte(), br.ReadByte(),
                                                                        br.ReadByte(), br.ReadByte(), br.ReadByte(), br.ReadByte() //12 total
                                                                    } : new byte[] { 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 }),
                                                                    vsEnabled = (ver > 2 ? new byte[]
                                                                    {
                                                                        br.ReadByte(), br.ReadByte(), br.ReadByte(), br.ReadByte(),
                                                                        br.ReadByte(), br.ReadByte(), br.ReadByte(), br.ReadByte(),
                                                                        br.ReadByte(), br.ReadByte(), br.ReadByte(), br.ReadByte() //12 total
                                                                    } : new byte[] { 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 }),
                                                                    vsTIMES = (ver > 2 ? new byte[]
                                                                    {
                                                                        br.ReadByte(), br.ReadByte(), br.ReadByte(), br.ReadByte(),
                                                                        br.ReadByte(), br.ReadByte(), br.ReadByte(), br.ReadByte(),
                                                                        br.ReadByte(), br.ReadByte(), br.ReadByte(), br.ReadByte() //12 total
                                                                    } : new byte[] { 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 }),
                                                                    vsAMTS = (ver > 2 ? new byte[]
                                                                    {
                                                                        br.ReadByte(), br.ReadByte(), br.ReadByte(), br.ReadByte(),
                                                                        br.ReadByte(), br.ReadByte(), br.ReadByte(), br.ReadByte(),
                                                                        br.ReadByte(), br.ReadByte(), br.ReadByte(), br.ReadByte() //12 total
                                                                    } : new byte[] { 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 })
                                                                });
                                                            }
                                                            commandMeasures.Add(measure);
                                                        }
                                                        //percussion
                                                        limit = br.ReadInt32();
                                                        for (int i = 0; i < limit; i++)
                                                        {
                                                            List<PercNote> measure = new List<PercNote>();
                                                            for (int r = 0; r < 32; r++)
                                                            {
                                                                temp = br.ReadByte();
                                                                measure.Add(new PercNote { bass = (temp & 0x10) == 0x10, snare = (temp & 8) == 8, tom = (temp & 4) == 4, cymbal = (temp & 2) == 2, hihat = (temp & 1) == 1 });
                                                            }
                                                            percussionMeasures.Add(measure);
                                                        }
                                                        //instruments
                                                        limit = br.ReadInt32();
                                                        for (int i = 0; i < limit; i++)
                                                        {
                                                            Instrument ins = new Instrument();
                                                            ins.name = br.ReadString();
                                                            ins.attenuation = br.ReadInt32();
                                                            ins.feedback = br.ReadInt32();
                                                            for (int w = 0; w < 2; w++)
                                                            {
                                                                ins.waves[w].multi = br.ReadInt32();
                                                                ins.waves[w].attack = br.ReadInt32();
                                                                ins.waves[w].decay = br.ReadInt32();
                                                                ins.waves[w].sustain = br.ReadInt32();
                                                                ins.waves[w].release = br.ReadInt32();
                                                                ins.waves[w].ampmod = br.ReadBoolean();
                                                                ins.waves[w].half = br.ReadBoolean();
                                                                ins.waves[w].ksl = br.ReadInt32();
                                                                ins.waves[w].ksr = br.ReadBoolean();
                                                                ins.waves[w].sustone = br.ReadBoolean();
                                                                ins.waves[w].vibrato = br.ReadBoolean();
                                                            }
                                                            instruments.Add(ins);
                                                        }
                                                    }
                                                    else
                                                    {
                                                        throw new Exception("incorrect file version");
                                                    }
                                                }
                                                selectedMeasure = 0;
                                                selectedMeasureCell = -1;
                                                selectedNoteCell = -1;
                                                selectedCommand = -1;
                                                selectedInstrument = 0;
                                                Debug.WriteLine("opened!");
                                            }
                                        }
                                        break;
                                    case 2:
                                        using (SaveFileDialog saveFileDialog = new SaveFileDialog())
                                        {
                                            saveFileDialog.Filter = "FM project (*.fmp)|*.fmp";
                                            saveFileDialog.RestoreDirectory = true;

                                            if (saveFileDialog.ShowDialog() == DialogResult.OK)
                                            {

                                                using (BinaryWriter bw = new BinaryWriter(saveFileDialog.OpenFile()))
                                                {
                                                    //start with file version byte
                                                    bw.Write((byte)3); //version
                                                    bw.Write(songname);//name
                                                    bw.Write(author);//author
                                                    bw.Write(bpm);//bpm
                                                    bw.Write(fpb);//fpm
                                                    bw.Write(npm);//npm
                                                    bw.Write((byte)((useFrameCounting ? 1 : 0) | (pal ? 2 : 0) | (percdbl ? 4 : 0) | (percedit ? 8 : 0)));//bpm, pal, perc bools
                                                    bw.Write(songLayout.Count);//song layout
                                                    for (int m = 0; m < songLayout.Count; m++)
                                                    {
                                                        for (int i = 0; i < songLayout[0].Count; i++)
                                                            bw.Write(songLayout[m][i]);
                                                    }
                                                    //fm
                                                    for (int c = 0; c < 9; c++)
                                                    {
                                                        bw.Write(fmMeasures[c].Count);
                                                        for (int i = 0; i < fmMeasures[c].Count; i++)
                                                            for (int r = 0; r < 32; r++)
                                                            {
                                                                bw.Write(fmMeasures[c][i][r].note);
                                                                bw.Write(fmMeasures[c][i][r].vol);
                                                                bw.Write(fmMeasures[c][i][r].inst);
                                                            }
                                                    }
                                                    //psg
                                                    for (int c = 0; c < 3; c++)
                                                    {
                                                        bw.Write(psgMeasures[c].Count);
                                                        for (int i = 0; i < psgMeasures[c].Count; i++)
                                                            for (int r = 0; r < 32; r++)
                                                            {
                                                                bw.Write(psgMeasures[c][i][r].note);
                                                                bw.Write(psgMeasures[c][i][r].vol);
                                                            }
                                                    }
                                                    //noise
                                                    bw.Write(noiseMeasures.Count);
                                                    for (int i = 0; i < noiseMeasures.Count; i++)
                                                        for (int r = 0; r < 32; r++)
                                                        {
                                                            bw.Write(noiseMeasures[i][r].periodic);
                                                            bw.Write(noiseMeasures[i][r].vol);
                                                            bw.Write(noiseMeasures[i][r].rate);
                                                        }
                                                    //command
                                                    bw.Write(commandMeasures.Count);
                                                    for (int i = 0; i < commandMeasures.Count; i++)
                                                        for (int r = 0; r < 32; r++)
                                                        {
                                                            bw.Write(commandMeasures[i][r].jump);
                                                            bw.Write(commandMeasures[i][r].jumptargetmeasure);
                                                            bw.Write(commandMeasures[i][r].jumptargetnote);
                                                            bw.Write(commandMeasures[i][r].instrumentswap);
                                                            bw.Write(commandMeasures[i][r].instrument);
                                                            bw.Write(commandMeasures[i][r].percvolchange);
                                                            for (int j = 0; j < 5; j++)
                                                                bw.Write(commandMeasures[i][r].percVol[j]);
                                                            bw.Write(commandMeasures[i][r].detune);
                                                            for (int j = 0; j < 12; j++)
                                                                bw.Write(commandMeasures[i][r].detuneAMTS[j]);
                                                            for(int j = 0; j < 12; j++)
                                                                bw.Write(commandMeasures[i][r].vsEnabled[j]);
                                                            for (int j = 0; j < 12; j++)
                                                                bw.Write(commandMeasures[i][r].vsTIMES[j]);
                                                            for (int j = 0; j < 12; j++)
                                                                bw.Write(commandMeasures[i][r].vsAMTS[j]);
                                                        }
                                                    //percussion
                                                    bw.Write(percussionMeasures.Count);
                                                    for (int i = 0; i < percussionMeasures.Count; i++)
                                                        for (int r = 0; r < 32; r++)
                                                            bw.Write((byte)((percussionMeasures[i][r].bass ? 0x10 : 0) | (percussionMeasures[i][r].snare ? 8 : 0) | (percussionMeasures[i][r].tom ? 4 : 0) | (percussionMeasures[i][r].cymbal ? 2 : 0) | (percussionMeasures[i][r].hihat ? 1 : 0)));
                                                    //instruments
                                                    bw.Write(instruments.Count);
                                                    for (int i = 0; i < instruments.Count; i++)
                                                    {
                                                        bw.Write(instruments[i].name);
                                                        bw.Write(instruments[i].attenuation);
                                                        bw.Write(instruments[i].feedback);
                                                        for (int w = 0; w < 2; w++)
                                                        {
                                                            bw.Write(instruments[i].waves[w].multi);
                                                            bw.Write(instruments[i].waves[w].attack);
                                                            bw.Write(instruments[i].waves[w].decay);
                                                            bw.Write(instruments[i].waves[w].sustain);
                                                            bw.Write(instruments[i].waves[w].release);
                                                            bw.Write(instruments[i].waves[w].ampmod);
                                                            bw.Write(instruments[i].waves[w].half);
                                                            bw.Write(instruments[i].waves[w].ksl);
                                                            bw.Write(instruments[i].waves[w].ksr);
                                                            bw.Write(instruments[i].waves[w].sustone);
                                                            bw.Write(instruments[i].waves[w].vibrato);
                                                        }
                                                    }
                                                }
                                                Debug.WriteLine("saved!");
                                            }
                                        }
                                        break;
                                    case 3:
                                        string path = "";
                                        bool full = false;
                                        using (SaveFileDialog saveFileDialog = new SaveFileDialog())
                                        {
                                            saveFileDialog.Filter = "Player ROM (*.sms)|*.sms|Raw Track Data (*.fmb)|(*.fmb)";
                                            saveFileDialog.RestoreDirectory = true;

                                            if (saveFileDialog.ShowDialog() == DialogResult.OK)
                                            {
                                                path = saveFileDialog.FileName;
                                                full = saveFileDialog.FilterIndex == 1;
                                            }
                                        }
                                        if (path != "")
                                            export(path, full);
                                        break;
                                }
                            }
                            else
                            {
                                //Debug.WriteLine("clicked top left side");
                                //Debug.WriteLine(mx);
                                //24, 39, 54
                                if (my > 24 && my < 40)
                                {
                                    selectedString = 0; //name
                                }
                                else if (my > 39 && my < 55)
                                {
                                    selectedString = 1; //author
                                }
                                else if (my > 54 && my < 69)
                                {
                                    if (mx > 65)
                                    {
                                        //number
                                        /*if (useFrameCounting)
                                        {
                                            fpb += rightclicked ? -1 : 1;
                                            if (fpb < 1)
                                                fpb = 1;
                                        }
                                        else
                                        {
                                            bpm += rightclicked ? -10 : 10;
                                            if (bpm < 10)
                                                bpm = 10;
                                        }*/
                                        selectedNumber = 0;
                                    }
                                    else if (mx < 55)
                                        useFrameCounting = !useFrameCounting;
                                }
                                else if (my > 85 && my < 95)
                                {
                                    /*advance += rightclicked ? -1 : 1;
                                    if (advance < 0)
                                        advance = 0;*/
                                    selectedNumber = 1;
                                }
                                else if (my > 70 && my < 82)
                                {
                                    npm = rightclicked ? npm / 2 : npm * 2;
                                    if (npm > 32)
                                        npm = 32;
                                    if (npm < 4)
                                        npm = 4;
                                }
                                else if (my > 100 && my < 115)
                                {
                                    percdbl = !percdbl;
                                }
                                else if (my > 115 && my < 125)
                                {
                                    /*octave += rightclicked ? -1 : 1;
                                    if (octave < 0)
                                        octave = 0;
                                    if (octave > 6)
                                        octave = 6;*/
                                    selectedNumber = 2;
                                }
                                else if (my > 129 && my < 145)
                                    pal = !pal;
                                else if (my > 145 && my < 160)
                                    percedit = !percedit;
                                else if (my > secHeight - 15)
                                {
                                    //copypasta buttons
                                    if(mx > hdiv/2 && clipboard.type != Clipboard.CBType.NULL)
                                    {
                                        //paste!
                                        int i = 0;
                                        //start at whatever the top selection is!
                                        int selCol = selectedNoteCell % CELLSPERROW;
                                        if (songLayout[selectedMeasure][PERCBOOL] == 1 && selCol > 17 && selCol < 27 && clipboard.type == Clipboard.CBType.PERC)
                                        {
                                            //perc
                                            for (i = 0; i < clipboard.percc.Count; i++) //for loop here, for how many cells have been selected
                                            {
                                                if (selectedNoteCell / CELLSPERROW + i >= npm)
                                                    break;
                                                percussionMeasures[songLayout[selectedMeasure][PERCCELL]][selectedNoteCell / CELLSPERROW + i] = new PercNote { 
                                                    bass = clipboard.percc[i].bass,
                                                    cymbal = clipboard.percc[i].cymbal,
                                                    hihat = clipboard.percc[i].hihat,
                                                    snare = clipboard.percc[i].snare,
                                                    tom = clipboard.percc[i].tom
                                                };
                                            }
                                        }
                                        else if (selCol < 27 && clipboard.type == Clipboard.CBType.FM)
                                        {
                                            //FM
                                            for( i = 0; i < clipboard.fmc.Count; i++)
                                            {
                                                if (selectedNoteCell / CELLSPERROW + i >= npm)
                                                    break;
                                                fmMeasures[(selectedNoteCell % CELLSPERROW) / 3][songLayout[selectedMeasure][(selectedNoteCell % CELLSPERROW) / 3]][selectedNoteCell / CELLSPERROW + i] = new FMNote { 
                                                    inst = clipboard.fmc[i].inst, 
                                                    note = clipboard.fmc[i].note, 
                                                    vol = clipboard.fmc[i].vol 
                                                };
                                            }
                                        }
                                        else if (selCol == 27 && clipboard.type == Clipboard.CBType.COM)
                                        {
                                            //COM
                                            for( i = 0; i < clipboard.comc.Count; i++)
                                            {
                                                if (selectedNoteCell / CELLSPERROW + i >= npm)
                                                    break;
                                                commandMeasures[songLayout[selectedMeasure][COMCELL]][selectedNoteCell / CELLSPERROW + i] = new CommandNote
                                                {
                                                    detune = clipboard.comc[i].detune,
                                                    instrument = clipboard.comc[i].instrument,
                                                    instrumentswap = clipboard.comc[i].instrumentswap,
                                                    jump = clipboard.comc[i].jump,
                                                    jumptargetmeasure = clipboard.comc[i].jumptargetmeasure,
                                                    jumptargetnote = clipboard.comc[i].jumptargetnote,
                                                    percvolchange = clipboard.comc[i].percvolchange,
                                                    percVol = new byte[] { 0, 0, 0, 0, 0 },
                                                    detuneAMTS = new byte[] { 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 },
                                                    vsEnabled = new byte[] { 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 },
                                                    vsAMTS = new byte[] { 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 },
                                                    vsTIMES = new byte[] { 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 }
                                                };
                                                for (int e = 0; e < 5; e++)
                                                    commandMeasures[songLayout[selectedMeasure][COMCELL]][selectedNoteCell / CELLSPERROW + i].percVol[e] = clipboard.comc[i].percVol[e];
                                                for(int e = 0; e < 12; e++)
                                                {
                                                    commandMeasures[songLayout[selectedMeasure][COMCELL]][selectedNoteCell / CELLSPERROW + i].detuneAMTS[e] = clipboard.comc[i].detuneAMTS[e];
                                                    commandMeasures[songLayout[selectedMeasure][COMCELL]][selectedNoteCell / CELLSPERROW + i].vsEnabled[e] = clipboard.comc[i].vsEnabled[e];
                                                    commandMeasures[songLayout[selectedMeasure][COMCELL]][selectedNoteCell / CELLSPERROW + i].vsAMTS[e] = clipboard.comc[i].vsAMTS[e];
                                                    commandMeasures[songLayout[selectedMeasure][COMCELL]][selectedNoteCell / CELLSPERROW + i].vsTIMES[e] = clipboard.comc[i].vsTIMES[e];
                                                }
                                            }

                                        }
                                        else if (selCol == 34 && clipboard.type == Clipboard.CBType.NOISE)
                                        {
                                            //noise
                                            for ( i = 0; i < clipboard.noisec.Count; i++)
                                            {
                                                if (selectedNoteCell / CELLSPERROW + i >= npm)
                                                    break;
                                                noiseMeasures[songLayout[selectedMeasure][13]][selectedNoteCell / CELLSPERROW + i] = new NoiseNote
                                                {
                                                    periodic = clipboard.noisec[i].periodic,
                                                    rate = clipboard.noisec[i].rate,
                                                    vol = clipboard.noisec[i].vol
                                                };
                                            }
                                        }
                                        else if (selCol < 34 && clipboard.type == Clipboard.CBType.PSG)
                                        {
                                            //psg
                                            for( i = 0; i < clipboard.psgc.Count; i++)
                                            {
                                                if (selectedNoteCell / CELLSPERROW + i >= npm)
                                                    break;
                                                psgMeasures[(selectedNoteCell % CELLSPERROW) / 2 - 14][songLayout[selectedMeasure][(selectedNoteCell % CELLSPERROW) / 2 - 14]][selectedNoteCell / CELLSPERROW + i] = new PSGNote
                                                {
                                                    note = clipboard.psgc[i].note,
                                                    vol = clipboard.psgc[i].vol
                                                };
                                            }
                                        }
                                        if(i > 0)
                                        {
                                            selectedNoteCell += i * CELLSPERROW;
                                        }


                                    }
                                    else if(selectedNoteCell != -1)
                                    {
                                        //copy!
                                        //first we figure out what was selected, then assign the type to clipboard and save the data
                                        int selCol = selectedNoteCell % CELLSPERROW;
                                        if (songLayout[selectedMeasure][PERCBOOL] == 1 && selCol > 17 && selCol < 27)
                                        {
                                            //perc
                                            clipboard.type = Clipboard.CBType.PERC;
                                            clipboard.percc = new List<PercNote>();
                                            for(int i = 0; i < vertCells; i++) //for loop here, for how many cells have been selected
                                            {
                                                PercNote note = percussionMeasures[songLayout[selectedMeasure][PERCCELL]][selectedNoteCell / CELLSPERROW + i]; //add the loop iterator after the division here
                                                clipboard.percc.Add(new PercNote { bass = note.bass, cymbal = note.cymbal, hihat = note.hihat, snare = note.snare, tom = note.tom });
                                            }
                                        }
                                        else if (selCol < 27)
                                        {
                                            //FM
                                            clipboard.type = Clipboard.CBType.FM;
                                            clipboard.fmc = new List<FMNote>();
                                            for(int i = 0; i < vertCells; i++)
                                            {
                                                FMNote note = fmMeasures[(selectedNoteCell % CELLSPERROW) / 3][songLayout[selectedMeasure][(selectedNoteCell % CELLSPERROW) / 3]][selectedNoteCell / CELLSPERROW + i];
                                                clipboard.fmc.Add(new FMNote { inst = note.inst, note = note.note, vol = note.vol });
                                            }
                                        }
                                        else if (selCol == 27)
                                        {
                                            //COM
                                            clipboard.type = Clipboard.CBType.COM;
                                            clipboard.comc = new List<CommandNote>();
                                            for(int i = 0; i < vertCells; i++)
                                            {
                                                CommandNote note = commandMeasures[songLayout[selectedMeasure][COMCELL]][selectedNoteCell / CELLSPERROW + i];
                                                clipboard.comc.Add(new CommandNote
                                                {
                                                    detune = note.detune, instrument = note.instrument, instrumentswap = note.instrumentswap, jump = note.jump,
                                                    jumptargetmeasure = note.jumptargetmeasure, jumptargetnote = note.jumptargetnote, percvolchange = note.percvolchange,
                                                    percVol = new byte[] { 0, 0, 0, 0, 0 },
                                                    detuneAMTS = new byte[] { 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 },
                                                    vsEnabled = new byte[] { 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 },
                                                    vsAMTS = new byte[] { 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 },
                                                    vsTIMES = new byte[] { 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 }
                                                });
                                                for (int e = 0; e < 5; e++)
                                                    clipboard.comc[i].percVol[e] = note.percVol[e];
                                                for (int e = 0; e < 12; e++)
                                                {
                                                    clipboard.comc[i].detuneAMTS[e] = note.detuneAMTS[e];
                                                    clipboard.comc[i].vsEnabled[e] = note.vsEnabled[e];
                                                    clipboard.comc[i].vsAMTS[e] = note.vsAMTS[e];
                                                    clipboard.comc[i].vsTIMES[e] = note.vsTIMES[e];
                                                }
                                            }

                                        }
                                        else if (selCol == 34)
                                        {
                                            //noise
                                            clipboard.type = Clipboard.CBType.NOISE;
                                            clipboard.noisec = new List<NoiseNote>();
                                            for(int i = 0; i < vertCells; i++)
                                            {
                                                NoiseNote note = noiseMeasures[songLayout[selectedMeasure][13]][selectedNoteCell / CELLSPERROW + i];
                                                clipboard.noisec.Add(new NoiseNote { periodic = note.periodic, rate = note.rate, vol = note.vol });
                                            }
                                        }
                                        else if (selCol < 34)
                                        {
                                            //psg
                                            clipboard.type = Clipboard.CBType.PSG;
                                            clipboard.psgc = new List<PSGNote>();
                                            for(int i = 0; i < vertCells; i++)
                                            {
                                                PSGNote note = psgMeasures[(selectedNoteCell % CELLSPERROW) / 2 - 14][songLayout[selectedMeasure][(selectedNoteCell % CELLSPERROW) / 2 - 14]][selectedNoteCell / CELLSPERROW + i];
                                                clipboard.psgc.Add(new PSGNote { note = note.note, vol = note.vol });
                                            }
                                        }
                                        //Debug.WriteLine(clipboard.type.ToString());
                                    }
                                }
                            }
                        }
                        else if (my > (instdiv - secHeight) / 8 + secHeight)
                        {
                            selectedInstrument = (my - secHeight) / ((instdiv - secHeight) / 8) - 1 + instrumentOffset;
                            if (selectedInstrument > instruments.Count - 1)
                                selectedInstrument = instruments.Count - 1;
                        }
                    }
                }
                else
                {
                    if (my < secHeight)
                    {
                        if (mx < topdiv)
                        {
                            selectedNoteCell = -1;
                            if (mx > hdiv + spacer * 1.5)
                                selectedMeasure = my / (secHeight / 8) + measureOffset;
                            if (selectedMeasure >= songLayout.Count())
                                selectedMeasure = songLayout.Count() - 1;
                            if (mx > hdiv + spacer * 2)
                            {
                                //clicked on a specific cell
                                selectedMeasureCell = (int)((mx - (hdiv + spacer * 2)) / (spacer / 2));
                                //Debug.WriteLine(selectedMeasureCell);
                                if (songLayout[selectedMeasure][PERCBOOL] == 1)
                                    if (selectedMeasureCell == 7 || selectedMeasureCell == 8)
                                        selectedMeasureCell = 6;
                            }
                            if (my / (secHeight / 8) == selectedMeasure - measureOffset)
                            {
                                if (mx > 275 && mx < 290 && selectedMeasure != 0)
                                    songLayout.RemoveAt(selectedMeasure);
                                if (mx > 295 && mx < 310)
                                    songLayout.Insert(selectedMeasure + 1, new List<int> { 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 });
                                if (selectedMeasure > songLayout.Count - 1)
                                    selectedMeasure = songLayout.Count - 1;
                                //Debug.WriteLine(mx.ToString() + ", " + my.ToString());
                            }
                        }
                        else
                        {
                            //Debug.WriteLine("clicked command editor");
                            selectedCommand = -1;
                            CommandNote cmd = commandMeasures[songLayout[selectedMeasure][COMCELL]][selectedNoteCell / CELLSPERROW];
                            int tx = mx;
                            tx -= topdiv;
                            //if (my < 67)
                            {
                                if (tx < 114) //toggle column?
                                {
                                    if (my < 16)
                                        cmd.percvolchange = !cmd.percvolchange;
                                    else if (my < 32)
                                        cmd.instrumentswap = !cmd.instrumentswap;
                                    else if (my < 48)
                                    {
                                        cmd.jump = !cmd.jump;
                                        /*if (tx > 28 && tx < 67)
                                        {
                                            cmd.percenable = !cmd.percenable;
                                            cmd.percdisable = false;
                                        }
                                        else if (tx > 69 && tx < 107)
                                        {
                                            cmd.percdisable = !cmd.percdisable;
                                            cmd.percenable = false;
                                        }*/
                                    }
                                    else if (my < 64)
                                        cmd.detune = !cmd.detune;
                                }
                                else
                                {
                                    //we're in the data entry area, get cell
                                    //0-4 is volumes, 5 is instrument, 6 is jump
                                    //and the clickable regions are 20x16
                                    int gridx = (tx - 114) / 20;
                                    int gridy = my / 16;
                                    if (gridx == 0 && gridy == 1)
                                        selectedCommand = 5;
                                    else if (gridx == 0 && gridy == 2)
                                        selectedCommand = 6;
                                    else if (gridy == 0 && gridx < 5)
                                        selectedCommand = gridx;
                                    else if (gridy == 3 && gridx < 12)
                                    {
                                        cmd.detuneAMTS[gridx] += (byte)(rightclicked ? -1 : 1);
                                    }
                                    else if (gridy == 4)
                                    {
                                        int cx = (tx - 114) / 22;
                                        cmd.vsEnabled[cx] = (byte)((cmd.vsEnabled[cx] + 1) % 3);
                                    }
                                    else if (gridy < 7)
                                    {
                                        selectedNumber = 17 + (gridy - 5) * 12 + (tx - 114) / 22;
                                        Debug.WriteLine(selectedNumber);
                                    }
                                    //Debug.WriteLine(selectedCommand.ToString());
                                }
                            }


                        }
                    }
                    else
                    {
                        selectedNoteCell = (int)((my - secHeight) / noteHeight) * CELLSPERROW;
                        int tx = mx;
                        tx -= hdiv;
                        if(tx < spacer*9)
                        {
                            //we're in FM
                            selectedNoteCell += (int)(tx / spacer)*3;
                            tx %= (int)spacer;
                            if (vertCells == 1)
                            {
                                if (tx > spacer / 2)
                                    selectedNoteCell++;
                                if (tx > spacer * 3 / 4 && (songLayout[selectedMeasure][PERCBOOL] == 0 || (selectedNoteCell % CELLSPERROW) < 18))
                                    selectedNoteCell++;
                            }
                        }
                        else if (tx < spacer*10)
                        {
                            selectedNoteCell += 27;
                        }
                        else
                        {
                            //we can treat psg and noise the same
                            selectedNoteCell += 28;
                            tx -= (int)(spacer * 10);
                            selectedNoteCell += (int)(tx / spacer)*2;
                            if (vertCells == 1)
                            {
                                if ((tx%(int)spacer) > spacer / 2)
                                    selectedNoteCell++;
                                if (tx > (int)(spacer * 3.75))
                                    selectedNoteCell++;
                            }
                        }
                        //Debug.WriteLine(selectedNoteCell.ToString());
                    }
                }
                clicked = false;
                rightclicked = false;
            }

            //handle scroll
            if(scrolldelta != 0)
            {
                if (my < secHeight && mx < topdiv && mx > hdiv)
                {
                    measureOffset += scrolldelta;
                    if (measureOffset > songLayout.Count - 8)
                        measureOffset = songLayout.Count - 8;
                    if (measureOffset < 0)
                        measureOffset = 0;
                }
                else if (mx < hdiv && my > secHeight && my < instdiv)
                {
                    instrumentOffset += scrolldelta;
                    if (instrumentOffset > instruments.Count - 7)
                        instrumentOffset = instruments.Count - 7;
                    if (instrumentOffset < 0)
                        instrumentOffset = 0;
                }
            }

            //bound some values
            if (selectedNoteCell >= CELLSPERROW * npm)
                selectedNoteCell = -1;
            scrolldelta = 0;

        }

        void draw()
        {
            g.FillRectangle(new SolidBrush(Color.Black), ClientRectangle);
            //draw backgrounds
            g.FillRectangle(fmbg, hdiv, secHeight, (int)(spacer * 6.0), (ClientSize.Height - secHeight) * npm / 32);
            g.FillRectangle(fmbg, hdiv + (int)(spacer*2), 0, (int)(spacer * 3), secHeight);
            //draw fm/perc area background
            if (songLayout[selectedMeasure][PERCBOOL] != 0)
                g.FillRectangle(percbg, hdiv + (int)(spacer * 6.0), secHeight, (int)(spacer * 3.0), (ClientSize.Height - secHeight) * npm / 32);
            else
                g.FillRectangle(fmbg, hdiv + (int)(spacer * 6.0), secHeight, (int)(spacer * 3.0), (ClientSize.Height - secHeight) * npm / 32);
            g.FillRectangle(psgbg, hdiv + (int)(spacer * 10), secHeight, (int)(spacer * 4.0), (ClientSize.Height - secHeight) * npm / 32);
            g.FillRectangle(psgbg, hdiv + (int)(spacer * 7), 0, (int)(spacer * 2), secHeight);
            for (int i = 0; i < Math.Min(songLayout.Count, 8); i++)
            {
                if (songLayout[i+measureOffset][PERCBOOL] == 0)
                    g.FillRectangle(fmbg, hdiv + (int)(spacer * 5.0), secHeight*i/8, (int)(spacer * 1.5)+1, secHeight/8);
                else
                    g.FillRectangle(percbg, hdiv + (int)(spacer * 5.0), secHeight * i / 8, (int)(spacer * 1.5) + 1, secHeight / 8);
            }
            for (int i = 0; i < 32; i += 2)
                g.FillRectangle(shade, hdiv, secHeight + (ClientSize.Height - secHeight) * i / 32, (int)(spacer * 14), (int)noteHeight);

            //highlight selected elements
            if (selectedMeasure - measureOffset > -1 && selectedMeasure - measureOffset < 8)
            {
                if (selectedMeasureCell == -1)
                    g.FillRectangle(highlight, hdiv + (int)(spacer * 1.5), secHeight * (selectedMeasure - measureOffset) / 8, (int)(spacer * 7.5) + 1, secHeight / 8 + 1);
                else
                    g.FillRectangle(highlight, hdiv + (int)(spacer * 2 + selectedMeasureCell * spacer / 2), secHeight * (selectedMeasure - measureOffset) / 8, (int)(spacer * ((selectedMeasureCell == 6 && songLayout[selectedMeasure][PERCBOOL] == 1) ? 3 : 1) / 2) + 1, secHeight / 8 + 1);
            }
            if (selectedInstrument - instrumentOffset > -1 && selectedInstrument - instrumentOffset < 7)
                g.FillRectangle(highlight, 0, (instdiv - secHeight) * (selectedInstrument + 1 - instrumentOffset) / 8 + secHeight, hdiv, (instdiv - secHeight) / 8 + 1);
            if (selectedNoteCell != -1)
            {
                int cw = (int)spacer;
                int cx = selectedNoteCell % CELLSPERROW;
                if(cx < 27) //fm
                {
                    if (cx % 3 == 0)
                    {
                        cw = (int)(spacer / 2);
                        cx = (int)(spacer * cx / 3);
                    }
                    else
                    {
                        cw = (int)(spacer / ((songLayout[selectedMeasure][PERCBOOL]==0 || (selectedNoteCell%CELLSPERROW) < 18)?4:2));
                        if (cx % 3 == 1)
                            cx = (int)(spacer * (cx / 3) + spacer / 2);
                        else
                            cx = (int)(spacer * (cx / 3) + spacer * 3 / 4);
                    }
                    
                }
                else if (cx == 27)
                {
                    cx = 9*(int)spacer;
                }
                else //psg/noise
                {
                    if (cx < 35)
                    {
                        cw = (int)(spacer / 2);
                        cx = (int)(spacer / 2 * (cx - 8));
                    }
                    else
                    {
                        cw = (int)(spacer / 4);
                        cx = (int)(spacer / 4 * (cx + 19));
                    }
                }

                if (vertCells != 1)
                {
                    cw = (int)spacer;
                    if (songLayout[selectedMeasure][PERCBOOL] == 1 && (selectedNoteCell % CELLSPERROW) > 17)
                        cw = (int)(spacer * 3);
                }

                g.FillRectangle(highlight, hdiv + cx, secHeight + (int)(selectedNoteCell / CELLSPERROW * noteHeight), cw+1, (int)(noteHeight * vertCells + 1));
            }
            if(selectedCommand != -1)
            {
                if (selectedCommand == 5)
                    g.FillRectangle(highlight, topdiv + 115, 16, 20, 16);
                else if (selectedCommand == 6)
                    g.FillRectangle(highlight, topdiv + 115, 32, 20, 16);
                else
                    g.FillRectangle(highlight, topdiv + 115 + selectedCommand * 20, 0, 20, 16);
            }


            //draw the note divisions
            for (int i = 1; i < npm + 1; i++)
            {
                int secY = (int)(noteHeight * (float)i + secHeight);
                g.DrawLine(gp, hdiv, secY, ClientSize.Width, secY);
            }
            //divide the lower section horizontally
            for (int i = 0; i < 14; i++)
            {
                int secX = (int)(spacer * (float)i + hdiv);
                if (i > 0) //don't draw first divider
                    g.DrawLine(lp, secX, secHeight, secX, ClientSize.Height);
            }
            //lower section volume dividers
            for(int i = 0; i < 14; i++)
            {
                int secX = (int)(spacer * (float)i + hdiv + spacer/2);
                Pen p = gp;
                if (songLayout[selectedMeasure][PERCBOOL] == 1 && i > 5 && i < 10)
                    p = lp;
                if (i != 9) //skip command
                    g.DrawLine(p, secX, secHeight, secX, (ClientSize.Height - secHeight) * npm / 32 + secHeight);
            }
            //noise rate line
            {
                int secX = (int)(spacer * (float)13 + hdiv + spacer * 3 / 4);
                g.DrawLine(gp, secX, secHeight, secX, (ClientSize.Height - secHeight) * npm / 32 + secHeight);
            }
            //fm instrument dividers
            for(int i = 0; i < (songLayout[selectedMeasure][PERCBOOL]==1?6:9); i++)
            {
                int secX = (int)(spacer * (float)i + hdiv + spacer * 3 / 4);
                g.DrawLine(gp, secX, secHeight, secX, (ClientSize.Height - secHeight) * npm / 32 + secHeight);
            }

            //pattern editor lines
            for (int i = 1; i < 8; i++)
                g.DrawLine(gp, hdiv + (int)(spacer * 2), secHeight * i / 8, topdiv, secHeight * i / 8);
            for (int i = -1; i < 14; i++)
                g.DrawLine(gp, hdiv + (int)(spacer * (i+4) /2), 0, hdiv + (int)(spacer * (i + 4) / 2), secHeight);

            //custom instrument list lines
            for (int i = 1; i < 8; i++)
                g.DrawLine(gp, 0, (instdiv - secHeight) * i / 8 + secHeight, hdiv, (instdiv - secHeight) * i / 8 + secHeight);


            //draw dividers
            g.DrawLine(lp, hdiv, 0, hdiv, ClientSize.Height);
            g.DrawLine(lp, 0, secHeight, ClientSize.Width, secHeight);
            g.DrawLine(lp, 0, instdiv, hdiv, instdiv);
            g.DrawLine(lp, topdiv, 0, topdiv, secHeight);

            //button dividers
            g.DrawLine(lp, 0, 22, hdiv, 22);
            for (int i = 1; i < 4; i++)
                g.DrawLine(lp, (int)(i * (hdiv / 4.0)), 0, (int)(i * (hdiv / 4.0)), 22);

            g.DrawLine(lp, 0, secHeight - 15, hdiv, secHeight - 15);
            g.DrawLine(lp, hdiv / 2, secHeight - 15, hdiv / 2, secHeight);


            //temp label text
            g.DrawString((selectedString == 0 ? ">" : "") + songname, f, sb, 2, 24);
            g.DrawString((selectedString == 1 ? ">" : "") + author, f, sb, 2, 39);
            if (useFrameCounting)
                g.DrawString("Song FPN:   " + (selectedNumber == 0?">":"") + fpb.ToString(), f, sb, 2, 54);
            else
                g.DrawString("Song BPM: " + (selectedNumber == 0 ? ">" : "") + bpm.ToString(), f, sb, 2, 54);
            g.DrawString("Notes Per Measure: " + npm.ToString(), f, sb, 2, 69);
            g.DrawString("Advance: " + (selectedNumber == 1 ? ">" : "") + advance.ToString(), f, sb, 2, 84);
            g.DrawString("Double Percussion Resolution: "+(percdbl? "✓" : "X"), f, sb, 2, 99);
            g.DrawString("Octave: " + (selectedNumber == 2 ? ">" : "") + octave.ToString(), f, sb, 2, 114);
            g.DrawString("Use PAL HZ: " + (pal? "✓" : "X"), f, sb, 2, 129);
            g.DrawString("Allow Perc Register Modification: " + (percedit? "✓":"X"), f, sb, 2, 144);

            g.DrawString("Copy notes", f, sb, 18, secHeight - 15);
            g.DrawString("Paste notes", f, sb, hdiv/2 + 20, secHeight - 15);

            g.DrawString("Custom Instruments", f, sb, 40, secHeight);

            //interface text
            g.DrawString("New", f, sb, 12, 2);
            g.DrawString("Open", f, sb, 60, 2);
            g.DrawString("Save", f, sb, 112, 2);
            g.DrawString("Export", f, sb, 156, 2);

            //command panel
            g.FillRectangle(fmbg, topdiv + 114, 48, 22 * 9, 16 * 4); //fm detune bg
            g.FillRectangle(psgbg, topdiv + 114 + 22 * 9, 48, 22 * 3, 16 * 4); //psg detune bg
            if (selectedNumber > 16 && selectedNumber < 41)
                g.FillRectangle(highlight, topdiv + 113 + (selectedNumber - 17) % 12 * 22, 16 * (5 + (selectedNumber - 17) / 12), 21, 16);
            CommandNote curcmd = commandMeasures[songLayout[selectedMeasure][COMCELL]][selectedNoteCell / CELLSPERROW];
            g.DrawString("Jump", f, curcmd.jump?sb:gb, topdiv + 2, 32);
            g.DrawString(curcmd.jumptargetmeasure.ToString(), f, curcmd.jump ? sb : gb, topdiv + 116, 31);
            g.DrawString("Inst Change", f, curcmd.instrumentswap?sb:gb, topdiv + 2, 16);
            g.DrawString(curcmd.instrument.ToString(), f, curcmd.instrumentswap ? sb : gb, topdiv + 116, 15);
            g.DrawString("Perc vol change", f, curcmd.percvolchange?sb:gb, topdiv + 2, 0);
            for (int i = 0; i < 5; i++)
                g.DrawString(curcmd.percVol[i].ToString("X"), f, curcmd.percvolchange ? sb : gb, topdiv + 116 + i*22, -1);
            g.DrawString("Note Detune", f, curcmd.detune ? sb : gb, topdiv + 2, 48);
            for (int i = 0; i < 12; i++)
                g.DrawString(((sbyte)curcmd.detuneAMTS[i]).ToString(), f, curcmd.detune ? sb : gb, topdiv + 116 + i * 22, 47);
            g.DrawString("Vibrato/Slide", f, sb, topdiv + 2, 64);
            g.DrawString("[note length]", f, sb, topdiv + 2, 80);
            g.DrawString("[note depth]", f, sb, topdiv + 2, 96);
            for (int i = 0; i < 12; i++)
            {
                g.DrawString(curcmd.vsEnabled[i]!= 0 ? (curcmd.vsEnabled[i] == 1 ? "V" : "S") : "X", f, curcmd.vsEnabled[i] != 0 ? sb : gb, topdiv + 116 + i * 22, 64);
                g.DrawString(curcmd.vsTIMES[i].ToString(), f, curcmd.vsEnabled[i] != 0 ? sb : gb, topdiv + 116 + i * 22, 80);
                g.DrawString(((curcmd.vsAMTS[i] & 0x7F) - (curcmd.vsEnabled[i] != 2 ? 0 : 31)).ToString(), f, curcmd.vsEnabled[i] != 0 ? sb : gb, topdiv + 116 + i * 22, 96);
            }

            g.DrawLine(gp, topdiv + 114, 0, topdiv + 114, 111);
            g.DrawLine(gp, topdiv + 134, 0, topdiv + 134, 111);
            for (int i = 1; i < 5; i++)
                g.DrawLine(gp, topdiv + 134 + 20 * i, 0, topdiv + 134 + 20 * i, 15);
            for (int i = 1; i < 12; i++)
                g.DrawLine(gp, topdiv + 134 + 22 * i, 48, topdiv + 134 + 22 * i, 111);

            g.DrawLine(gp, topdiv + 1, 15, topdiv + 214, 15);
            g.DrawLine(gp, topdiv + 1, 31, topdiv + 134, 31);
            for(int i = 0; i < 5; i++)
            g.DrawLine(gp, topdiv + 1, 47 + i*16, topdiv + 112+12*22, 47 + i*16);

            //draw param divider at 110

            //layout editor
            for (int i = 0; i < Math.Min(songLayout.Count, 8); i++)
            {
                g.DrawString((i + measureOffset).ToString(), f, sb, hdiv + (int)(spacer * 1.5) + 14, secHeight * i / 8 + 4);
                if (i + measureOffset == selectedMeasure)
                    g.DrawString("-     +", f, sb, hdiv + (int)(spacer), secHeight * i / 8 + 4);
                for (int j = 0; j < 14; j++)
                {
                    if (songLayout[i + measureOffset][PERCBOOL] == 1)
                    {
                        if (j == 6 || j == 8)
                            continue;
                        if (j == 7)
                            g.DrawString(songLayout[i + measureOffset][PERCCELL].ToString(), f, sb, hdiv + (int)(spacer * (j + 4) / 2) + 14, secHeight * i / 8 + 4);
                        else
                            g.DrawString(songLayout[i + measureOffset][j].ToString(), f, sb, hdiv + (int)(spacer * (j + 4) / 2) + 14, secHeight * i / 8 + 4);
                    }
                    else
                        g.DrawString(songLayout[i + measureOffset][j].ToString(), f, sb, hdiv + (int)(spacer * (j + 4) / 2) + 14, secHeight * i / 8 + 4);
                }
            }
            //instruments
            for (int i = 0; i < Math.Min(instruments.Count, 7); i++)
                g.DrawString((i + instrumentOffset).ToString("X") + ": " + instruments[i + instrumentOffset].name, f, sb, 2, (instdiv - secHeight) * (i + 1) / 8 + secHeight);

            //instrument panel
            g.DrawLine(lp, 0, instdiv + 15, hdiv, instdiv + 15);
            g.DrawLine(lp, hdiv / 2, instdiv, hdiv / 2, instdiv + 15);
            g.DrawString("Add", f, sb, hdiv / 6, instdiv);
            g.DrawString("Remove", f, sb, hdiv / 3 * 2, instdiv);
            //global inst data
            {
                Instrument inst = instruments[selectedInstrument];
                g.DrawString("Name: " + (selectedString == 2 ? ">" : "") + inst.name, f, sb, 2, instdiv + 16);
                g.DrawString("Attenuation: " + (selectedNumber == 3 ? ">" : "") + inst.attenuation.ToString(), f, sb, 2, instdiv + 32);
                g.DrawString("Feedback: " + (selectedNumber == 4 ? ">" : "") + inst.feedback.ToString(), f, sb, hdiv / 2 + 2, instdiv + 32);
                //draw each wave
                for(int i = 0; i < 2; i++)
                {
                    Instrument.Wave w = inst.waves[i];
                    int voff = instdiv + 48 + i*WAVOFF; //plus the height of the section, in the future?
                    g.DrawLine(gp, 0, voff, hdiv, voff);
                    g.DrawString(i == CARRIER ? "Carrier" : "Modulator", f, sb, 2, voff);
                    g.DrawString("A: " + (selectedNumber == 5 + i*6 ? ">" : "") + w.attack.ToString("X"), f, w.attack == 0?rb:sb, 2, voff + 16);
                    g.DrawString("D: " + (selectedNumber == 6 + i*6 ? ">" : "") + w.decay.ToString("X"), f, sb, hdiv / 4 + 2, voff + 16);
                    g.DrawString("S: " + (selectedNumber == 7 + i * 6 ? ">" : "") + w.sustain.ToString("X"), f, sb, hdiv/2 + 2, voff + 16);
                    g.DrawString("R: " + (selectedNumber == 8 + i * 6 ? ">" : "") + w.release.ToString("X"), f, sb, hdiv *3 / 4 + 2, voff + 16);
                    g.DrawString("Multiplier: " + (selectedNumber == 9 + i * 6 ? ">" : "") + w.multi.ToString("X"), f, sb, 2, voff + 32);
                    g.DrawString("Level Scale: " + (selectedNumber == 10 + i * 6 ? ">" : "") + w.ksl.ToString(), f, sb, hdiv / 2 + 2, voff + 32);
                    g.DrawString("Sustained: " + (w.sustone ? "✓" : "X"), f, sb, 2, voff + 48);
                    g.DrawString("Scale Rate: " + (w.ksr ? "✓" : "X"), f, sb, hdiv / 2 + 2, voff + 48);
                    g.DrawString("Vibrato: " + (w.vibrato ? "✓" : "X"), f, sb, 2, voff + 64);
                    g.DrawString("AM: " + (w.ampmod ? "✓" : "X"), f, sb, hdiv / 2 + 2, voff + 64);
                    g.DrawString("Half: " + (w.half ? "✓" : "X"), f, sb, hdiv * 3 / 4 + 2, voff + 64);
                    g.DrawLine(gp, 0, voff + 80, hdiv, voff + 80);
                    g.DrawString("(wave visual here??)", f, gb, 33, voff + 122);
                }
            }

            //notes
            for (int i = 0; i < npm; i++)
            {
                List<int> mRow = songLayout[selectedMeasure];
                int textheight = secHeight + (ClientSize.Height - secHeight) * i / 32;
                //fm
                for (int j = 0; j < (mRow[PERCBOOL]==1?6:9); j++)
                {
                    if (fmMeasures[j][mRow[j]][i].vol == -1)
                        g.DrawString("--", f, sb, hdiv + (int)(spacer * j), textheight);
                    else
                    {
                        FMNote n = fmMeasures[j][mRow[j]][i];
                        string s = "";
                        if (n.note > 127)
                        {
                            s = getNoteName(n.note - 256);
                            s += " X";
                        }
                        else
                            s = getNoteName(n.note);
                        g.DrawString(s, f, sb, hdiv + (int)(spacer * j), textheight);
                        g.DrawString(n.vol.ToString("X"), f, sb, hdiv + (int)(spacer * j + spacer / 2), textheight);
                        g.DrawString(n.inst.ToString("X"), f, sb, hdiv + (int)(spacer * j + spacer * 3 / 4), textheight);
                    }
                }
                //perc
                if(mRow[PERCBOOL] == 1)
                {
                    for(int j = 0; j < 5; j++)
                    {
                        SolidBrush b = gb;
                        PercNote perc = percussionMeasures[mRow[PERCCELL]][i];
                        if ((j == 0 && perc.bass) || (j == 1 && perc.snare) || (j == 2 && perc.tom) || (j == 3 && perc.cymbal) || (j == 4 && perc.hihat))
                            b = sb;
                        g.DrawString("BSTCH".Substring(j, 1), f, b, hdiv + (int)(spacer * (j + 12.3) / 2), textheight);
                    }
                }
                //psg
                for (int j = 0; j < 3; j++)
                {
                    PSGNote n = psgMeasures[j][mRow[j + 10]][i];
                    if (n.vol == -1)
                        g.DrawString("--", f, sb, hdiv + (int)(spacer * (j + 10)), textheight);
                    else
                    {
                        g.DrawString(getNoteName(n.note), f, sb, hdiv + (int)(spacer * (j + 10)), textheight);
                        g.DrawString(n.vol.ToString("X"), f, sb, hdiv + (int)(spacer * (j + 10) + spacer/2), textheight);
                    }
                }
                //noise
                {
                    NoiseNote n = noiseMeasures[mRow[13]][i];
                    if (n.vol == -1)
                        g.DrawString("--", f, sb, hdiv + (int)(spacer * 13), textheight);
                    else
                    {
                        g.DrawString(n.periodic ? "PER" : "RND", f, sb, hdiv + (int)(spacer * 13), textheight);
                        g.DrawString(n.vol.ToString("X"), f, sb, hdiv + (int)(spacer * 13.5), textheight);
                        g.DrawString(n.rate.ToString("X"), f, sb, hdiv + (int)(spacer * 13.75), textheight);
                    }
                }
                //commands
                CommandNote cmd = commandMeasures[mRow[COMCELL]][i];
                string cmds = "";
                if (cmd.instrumentswap || cmd.jump || cmd.percvolchange || cmd.detune)
                    cmds = "CMD";
                else
                    for(int e = 0; e < 12; e++)
                        if(cmd.vsEnabled[e] != 0)
                        {
                            cmds = "CMD";
                            break;
                        }
                g.DrawString(cmds, f, sb, hdiv + (int)(spacer * 9), textheight);
            }
            BackgroundImage = imgBuf;
            Invalidate();
        }

        private void clickhandler(object sender, MouseEventArgs e)
        {
            mx = e.X;
            my = e.Y;
            downx = e.X; //TODO: these shift around with the drag. hmmmmm
            downy = e.Y;
            if (mx > hdiv || my > secHeight || my < secHeight - 15)
                vertCells = 1;
            if (e.Button == MouseButtons.Left || e.Button == MouseButtons.Right)
            {
                //store position so we can handle it in update?
                clicked = true;
                if (e.Button == MouseButtons.Right)
                    rightclicked = true;
            }
        }

        private void draghandler(object sender, MouseEventArgs e)
        {
            if(e.Button == MouseButtons.Left || e.Button == MouseButtons.Right)
            {
                /*if (downy > secHeight && downx > hdiv)
                {
                    vertCells = (int)(Math.Abs((int)((my - secHeight) / noteHeight) - (int)((downy - secHeight) / noteHeight)) + 1);
                }*/
                //Debug.WriteLine("vert cnt: " + vertCells.ToString());
                //Debug.WriteLine("mouse y start: " + ((int)((my - secHeight) / noteHeight)).ToString());
                //Debug.WriteLine("mouse y end: " + ((int)((downy - secHeight) / noteHeight)).ToString());
                downx = 0;
                downy = 0;
            }
        }

        private void movehandler(object sender, MouseEventArgs e)
        {
            if(downx != 0 && downy != 0)//currently in a click
            {
                downx = e.X;
                downy = e.Y;
                if(downy > secHeight && downx > hdiv)//should we update the drag?
                {
                    //if current y value is less than starting y, swap them
                    if (my > downy)
                    {
                        int t = downy;
                        downy = e.Y;
                        my = t;
                    }
                    else if (downy > noteHeight * (npm - 1) + secHeight)
                        downy = (int)(noteHeight * (npm - 1) + secHeight + 8);
                    //determine the vertcount here
                    vertCells = (int)(Math.Abs((int)((my - secHeight) / noteHeight) - (int)((downy - secHeight) / noteHeight)) + 1);
                    //and set to clicked
                    clicked = true;
                }
            }
        }

        private void wheelhandler(object sender, MouseEventArgs e)
        {
            scrolldelta = e.Delta/-120;
        }

        private void focuslost(object sender, EventArgs e)
        {
            downx = 0;
            downy = 0;
            clicked = false;
        }

        private void keyhandler(object sender, KeyEventArgs e)
        {
            Keys k = e.KeyCode;
            if(selectedNumber != -1)
            {
                //number input code here
                if (k >= Keys.D0 && k <= Keys.D9)
                {
                    k -= Keys.D0;
                    switch (selectedNumber)
                    {
                        case 0: //fpm/bpm
                            if(useFrameCounting)
                            {
                                fpb = fpb * 10 + (int)k;
                                if (fpb > 60)
                                    fpb = 60;
                            }
                            else
                            {
                                bpm = bpm * 10 + (int)k;
                                if (bpm > 360)
                                    bpm = 360;
                            }
                            break;
                        case 1: //advance
                            advance = advance * 10 + (int)k;
                            if (advance > 32)
                                advance = 32;
                            break;
                        case 2: //octave
                            octave = (int)k;
                            if (octave > 7)
                                octave = 7;
                            break;
                        case 3: //inst atten (0 - 63)
                            instruments[selectedInstrument].attenuation = instruments[selectedInstrument].attenuation * 10 + (int)k;
                            if (instruments[selectedInstrument].attenuation > 63)
                                instruments[selectedInstrument].attenuation = 63;
                            break;
                        case 4: //inst feedback (0 - 7)
                            instruments[selectedInstrument].feedback = (int)k;
                            if (instruments[selectedInstrument].feedback > 7)
                                instruments[selectedInstrument].feedback = 7;
                            break;
                        case 5: //inst attack (nybble)
                        case 11:
                            instruments[selectedInstrument].waves[(selectedNumber - 5) / 6].attack = (int)k;
                            break;
                        case 6: //inst decay (nybble)
                        case 12:
                            instruments[selectedInstrument].waves[(selectedNumber - 6) / 6].decay = (int)k;
                            break;
                        case 7: //inst sustain (nybble)
                        case 13:
                            instruments[selectedInstrument].waves[(selectedNumber - 7) / 6].sustain = (int)k;
                            break;
                        case 8: //inst release (nybble)
                        case 14:
                            instruments[selectedInstrument].waves[(selectedNumber - 8) / 6].release = (int)k;
                            break;
                        case 9: //inst mult (nybble)
                        case 15:
                            instruments[selectedInstrument].waves[(selectedNumber - 9) / 6].multi = (int)k;
                            break;
                        case 10: //inst ksl (0 - 3)
                        case 16:
                            instruments[selectedInstrument].waves[(selectedNumber - 10) / 6].ksl = (int)k;
                            if (instruments[selectedInstrument].waves[(selectedNumber - 10) / 6].ksl > 3)
                                instruments[selectedInstrument].waves[(selectedNumber - 10) / 6].ksl = 3;
                            break;
                        case 17:
                        case 18:
                        case 19:
                        case 20:
                        case 21:
                        case 22:
                        case 23:
                        case 24:
                        case 25:
                        case 26:
                        case 27:
                        case 28: //command vs length
                            commandMeasures[songLayout[selectedMeasure][COMCELL]][selectedNoteCell / CELLSPERROW].vsTIMES[selectedNumber-17] = 
                                (byte)Math.Min(commandMeasures[songLayout[selectedMeasure][COMCELL]][selectedNoteCell / CELLSPERROW].vsTIMES[selectedNumber - 17]*10 + (int)k, 0xFF);
                            break;
                        case 29: //command vs amount
                        case 30:
                        case 31:
                        case 32:
                        case 33:
                        case 34:
                        case 35:
                        case 36:
                        case 37:
                        case 38:
                        case 39:
                        case 40:
                            commandMeasures[songLayout[selectedMeasure][COMCELL]][selectedNoteCell / CELLSPERROW].vsAMTS[selectedNumber - 29] =
                                (byte)Math.Min(commandMeasures[songLayout[selectedMeasure][COMCELL]][selectedNoteCell / CELLSPERROW].vsAMTS[selectedNumber - 29] * 10 + (int)k, 0x3F);
                            break;
                    }
                }
                else if (k == Keys.Back)
                {
                    switch (selectedNumber)
                    {
                        case 0: //fpm/bpm
                            if (useFrameCounting)
                                fpb = (int)(fpb/10);
                            else
                                bpm = (int)(bpm/10);
                            break;
                        case 1: //advance
                            advance /= 10;
                            break;
                        case 3: //inst attenuation
                            instruments[selectedInstrument].attenuation /= 10;
                            break;
                        case 17:
                        case 18:
                        case 19:
                        case 20:
                        case 21:
                        case 22:
                        case 23:
                        case 24:
                        case 25:
                        case 26:
                        case 27:
                        case 28: //command vs length
                            commandMeasures[songLayout[selectedMeasure][COMCELL]][selectedNoteCell / CELLSPERROW].vsTIMES[selectedNumber - 17] /= 10;
                            break;
                        case 29: //command vs amount
                        case 30:
                        case 31:
                        case 32:
                        case 33:
                        case 34:
                        case 35:
                        case 36:
                        case 37:
                        case 38:
                        case 39:
                        case 40:
                            commandMeasures[songLayout[selectedMeasure][COMCELL]][selectedNoteCell / CELLSPERROW].vsAMTS[selectedNumber - 29] /= 10;
                            break;
                    }
                }
                else
                {
                    int hex = 0;
                    if (k >= Keys.A && k <= Keys.F)
                        hex = (int)k - 55;
                    if(hex != 0)
                    {
                        switch(selectedNumber)
                        {
                            case 5: //inst attack (nybble)
                            case 11:
                                instruments[selectedInstrument].waves[(selectedNumber - 5) / 6].attack = hex;
                                break;
                            case 6: //inst decay (nybble)
                            case 12:
                                instruments[selectedInstrument].waves[(selectedNumber - 6) / 6].decay = hex;
                                break;
                            case 7: //inst sustain (nybble)
                            case 13:
                                instruments[selectedInstrument].waves[(selectedNumber - 7) / 6].sustain = hex;
                                break;
                            case 8: //inst release (nybble)
                            case 14:
                                instruments[selectedInstrument].waves[(selectedNumber - 8) / 6].release = hex;
                                break;
                            case 9: //inst mult (nybble)
                            case 15:
                                instruments[selectedInstrument].waves[(selectedNumber - 9) / 6].multi = hex;
                                break;
                        }
                    }
                }

            }
            else if(selectedString != -1)
            {
                //string input code here
                string add = "";
                bool del = false;
                if (k >= Keys.A && k <= Keys.Z)
                    add = ((char)(k + (e.Shift ? 0 : 32))).ToString();
                else if (k >= Keys.D0 && k <= Keys.D9)
                    add = ((char)k).ToString();
                else if (k == Keys.Space)
                    add = " ";
                else if (k == Keys.Back)
                    del = true;
                else if (k == Keys.Return)
                    selectedString = -1;

                if(add != "")
                    switch (selectedString)
                    {
                        case 0:
                            if(songname.Length < 32)
                            songname += add;
                            break;
                        case 1:
                            if(author.Length < 16)
                            author += add;
                            break;
                        case 2:
                            instruments[selectedInstrument].name += add;
                            break;
                    }
                else if (del)
                    switch (selectedString)
                    {
                        case 0:
                            if (songname.Length > 0)
                                songname = songname.Remove(songname.Length - 1);
                            break;
                        case 1:
                            if (author.Length > 0)
                                author = author.Remove(author.Length - 1);
                            break;
                        case 2:
                            if (instruments[selectedInstrument].name.Length > 0)
                                instruments[selectedInstrument].name = instruments[selectedInstrument].name.Remove(instruments[selectedInstrument].name.Length - 1);
                            break;
                    }
            }
            else if (k == Keys.Enter)
            {
                //enter/exit playback mode
                //keep edit enabled if entering playback with shift held
                //always enable edit upon exiting playback
            }
            else if (k == Keys.Space)
            {
                //toggle edit mode
                //editing = !editing;
            }
            else
            {
                int selcol = selectedNoteCell % CELLSPERROW;
                int selrow = selectedNoteCell / CELLSPERROW;
                bool OFF = false;
                if (selectedMeasureCell != -1)
                {
                    //editing selected measure entry
                    int v = 0;
                    int kh = 0;
                    int kv = 0;
                    int n = -1;
                    bool del = false;
                    switch (k)
                    {
                        case Keys.Add:
                        case Keys.Oemplus:
                            v = 1;
                            break;
                        case Keys.Subtract:
                        case Keys.OemMinus:
                            v = -1;
                            break;
                        case Keys.Tab:
                            songLayout[selectedMeasure][PERCBOOL] = 1 - songLayout[selectedMeasure][PERCBOOL];
                            if (selectedMeasureCell == 7 || selectedMeasureCell == 8)
                                selectedMeasureCell = 6;
                            break;
                        case Keys.Up:
                            kv = -1;
                            break;
                        case Keys.Down:
                            kv = 1;
                            break;
                        case Keys.Left:
                            kh = -1;
                            break;
                        case Keys.Right:
                            kh = 1;
                            break;

                        case Keys.D0:
                        case Keys.D1:
                        case Keys.D2:
                        case Keys.D3:
                        case Keys.D4:
                        case Keys.D5:
                        case Keys.D6:
                        case Keys.D7:
                        case Keys.D8:
                        case Keys.D9:
                            n = k - Keys.D0;
                            break;

                        case Keys.Delete:
                        case Keys.Back:
                            del = true;
                            break;
                    }
                    selectedMeasure += kv;
                    if (selectedMeasure < 0)
                        selectedMeasure = 0;
                    if (selectedMeasure > songLayout.Count - 1)
                        selectedMeasure = songLayout.Count - 1;
                    if (selectedMeasure < measureOffset)
                        measureOffset = selectedMeasure;
                    if (selectedMeasure > measureOffset + 7)
                        measureOffset = selectedMeasure - 7;
                    selectedMeasureCell += kh;
                    if (selectedMeasureCell < -1)
                        selectedMeasureCell = -1;
                    if (selectedMeasureCell > 14)
                        selectedMeasureCell = 14;
                    if (selectedMeasureCell == -1)
                    {
                        e.Handled = true;
                        return;
                    }

                    songLayout[selectedMeasure][(selectedMeasureCell == 6 && songLayout[selectedMeasure][PERCBOOL] == 1)?PERCCELL:selectedMeasureCell] += v;
                    if (n != -1)
                        songLayout[selectedMeasure][selectedMeasureCell] = songLayout[selectedMeasure][selectedMeasureCell] * 10 + n;
                    if (del)
                        songLayout[selectedMeasure][selectedMeasureCell] /= 10;
                    if (songLayout[selectedMeasure][(selectedMeasureCell == 6 && songLayout[selectedMeasure][PERCBOOL] == 1) ? PERCCELL : selectedMeasureCell] < 0)
                        songLayout[selectedMeasure][(selectedMeasureCell == 6 && songLayout[selectedMeasure][PERCBOOL] == 1) ? PERCCELL : selectedMeasureCell] = 0;
                    if (selectedMeasureCell < (songLayout[selectedMeasure][PERCBOOL] == 1 ? 6 : 9))
                    {
                        if (fmMeasures[selectedMeasureCell].Count - 1 < songLayout[selectedMeasure][selectedMeasureCell])
                        {
                            songLayout[selectedMeasure][selectedMeasureCell] = fmMeasures[selectedMeasureCell].Count;
                            addFMMeasure(selectedMeasureCell);
                        }
                    }
                    else if (selectedMeasureCell > 5 && selectedMeasureCell < 9 && songLayout[selectedMeasure][PERCBOOL] == 1)
                    {
                        if (percussionMeasures.Count - 1 < songLayout[selectedMeasure][PERCCELL])
                        {
                            songLayout[selectedMeasure][selectedMeasureCell] = percussionMeasures.Count;
                            addPercussionMeasure();
                        }
                    }
                    else if (selectedMeasureCell == 9)
                    {
                        if (commandMeasures.Count - 1 < songLayout[selectedMeasure][selectedMeasureCell])
                        {
                            songLayout[selectedMeasure][selectedMeasureCell] = commandMeasures.Count;
                            addCommandMeasure();
                        }
                    }
                    else if (selectedMeasureCell > 9 && selectedMeasureCell < 13)
                    {
                        if (psgMeasures[selectedMeasureCell - 10].Count - 1 < songLayout[selectedMeasure][selectedMeasureCell])
                        {
                            songLayout[selectedMeasure][selectedMeasureCell] = psgMeasures[selectedMeasureCell-10].Count;
                            addPSGMeasure(selectedMeasureCell - 10);
                        }
                    }
                    else if (selectedMeasureCell == 13)
                    {
                        if (noiseMeasures.Count - 1 < songLayout[selectedMeasure][selectedMeasureCell])
                        {
                            songLayout[selectedMeasure][selectedMeasureCell] = noiseMeasures.Count;
                            addNoiseMeasure();
                        }
                    }
                }
                else if (selectedMeasureCell == -1 && selectedNoteCell == -1)
                {
                    int kh = 0;
                    int kv = 0;
                    switch (k)
                    {
                        case Keys.Up:
                            kv = -1;
                            break;
                        case Keys.Down:
                            kv = 1;
                            break;
                        case Keys.Right:
                            kh = 1;
                            break;
                    }
                    selectedMeasureCell += kh;
                    selectedMeasure += kv;
                    if (selectedMeasure < 0)
                        selectedMeasure = 0;
                    if (selectedMeasure > songLayout.Count - 1)
                        selectedMeasure = songLayout.Count - 1;
                    if (selectedMeasure < measureOffset)
                        measureOffset = selectedMeasure;
                    if (selectedMeasure > measureOffset + 7)
                        measureOffset = selectedMeasure - 7;
                }
                if(selectedNoteCell != -1)
                {
                    //handle arrow keys here
                    int kv = 0;
                    int kh = 0;
                    switch(k)
                    {
                        case Keys.Up:
                            kv = -1;
                            break;
                        case Keys.Down:
                            kv = 1;
                            break;
                        case Keys.Left:
                            kh = -1;
                            break;
                        case Keys.Right:
                            kh = 1;
                            break;
                    }

                    selectedNoteCell += kh;
                    if (selectedNoteCell < 0)
                        selectedNoteCell = 0;
                    if (kh == 1 && (selectedNoteCell % CELLSPERROW) == 0)
                        selectedNoteCell--;
                    if (kh == -1 && (selectedNoteCell % CELLSPERROW) == CELLSPERROW - 1)
                        selectedNoteCell++;
                    selectedNoteCell += kv * CELLSPERROW;
                    if (selectedNoteCell < 0)
                        selectedNoteCell += CELLSPERROW;
                    if (selectedNoteCell / CELLSPERROW == npm)
                        selectedNoteCell -= CELLSPERROW;

                }
                if (selectedCommand != -1)
                {
                    //we have a command cell selected, interpret numbers for them
                    int v = -1;
                    switch (k)
                    {
                        case Keys.D0:
                        case Keys.D1:
                        case Keys.D2:
                        case Keys.D3:
                        case Keys.D4:
                        case Keys.D5:
                        case Keys.D6:
                        case Keys.D7:
                        case Keys.D8:
                        case Keys.D9:
                            v = k - Keys.D0;
                            break;
                        case Keys.A:
                        case Keys.B:
                        case Keys.C:
                        case Keys.D:
                        case Keys.E:
                            v = k - Keys.A + 10;
                            break;
                        case Keys.F:
                        case Keys.Tab:
                        case Keys.Delete:
                        case Keys.Back:
                            v = 15;
                            break;
                    }
                    if (v != -1)
                    {
                        CommandNote note = commandMeasures[songLayout[selectedMeasure][COMCELL]][selectedNoteCell / CELLSPERROW];
                        if (selectedCommand == 6)
                        {
                            if (v < 10)
                                note.jumptargetmeasure = note.jumptargetmeasure * 10 + v;
                            else if (k == Keys.Delete || k == Keys.Back)
                                note.jumptargetmeasure /= 10;
                            if (note.jumptargetmeasure > 255)
                                note.jumptargetmeasure = 255;
                        }
                        else if (selectedCommand == 5)
                        {
                            if (v < 10)
                                note.instrument = note.instrument * 10 + v;
                            else if (k == Keys.Delete || k == Keys.Back)
                                note.instrument /= 10;
                            if (note.instrument > 255)
                                note.instrument = 255;
                        }
                        else if (selectedCommand < 5)
                            note.percVol[selectedCommand] = (byte)v;
                    }
                }
                else if ((selcol < (songLayout[selectedMeasure][PERCBOOL] == 1 ? 18 : 27) && (selcol % 3 == 0)) || (selcol > 27 && selcol != 36 && (selcol % 2 == 0)))
                {
                    //we are trying to edit a note, let's interpret the key as a note press
                    //System.Diagnostics.Debug.WriteLine("note edit");
                    int n = -12;
                    bool del = false;
                    int o = 0;
                    switch (k)
                    {
                        case Keys.Tab:
                            if (selcol < 27) //only possible for FM
                            {
                                n = fmMeasures[selcol / 3][songLayout[selectedMeasure][selcol / 3]][selrow].note;
                                if (n > 0)
                                    OFF = true;
                                else
                                    n = -12;
                            }
                            break;
                        case Keys.Delete:
                        case Keys.Back:
                            del = true;
                            break;
                        case Keys.Z:
                            n = -11;
                            break;
                        case Keys.S:
                            n = -10;
                            break;
                        case Keys.X:
                            n = -9;
                            break;
                        case Keys.D:
                            n = -8;
                            break;
                        case Keys.C:
                            n = -7;
                            break;
                        case Keys.V:
                            n = -6;
                            break;
                        case Keys.G:
                            n = -5;
                            break;
                        case Keys.B:
                            n = -4;
                            break;
                        case Keys.H:
                            n = -3;
                            break;
                        case Keys.N:
                            n = -2;
                            break;
                        case Keys.J:
                            n = -1;
                            break;
                        case Keys.M:
                            n = 0;
                            break;
                        case Keys.Q:
                        case Keys.Oemcomma:
                            n = 1;
                            break;
                        case Keys.D2:
                            n = 2;
                            break;
                        case Keys.W:
                            n = 3;
                            break;
                        case Keys.D3:
                            n = 4;
                            break;
                        case Keys.E:
                            n = 5;
                            break;
                        case Keys.R:
                            n = 6;
                            break;
                        case Keys.D5:
                            n = 7;
                            break;
                        case Keys.T:
                            n = 8;
                            break;
                        case Keys.D6:
                            n = 9;
                            break;
                        case Keys.Y:
                            n = 10;
                            break;
                        case Keys.D7:
                            n = 11;
                            break;
                        case Keys.U:
                            n = 12;
                            break;

                        case Keys.PageUp:
                            o = 1;
                            break;
                        case Keys.PageDown:
                            o = -1;
                            break;
                    }

                    if (!OFF && !del && n != -12)
                        n += octave * 12;
                    if (n > 128)
                    {
                        OFF = false;
                        n -= 256;
                    }
                    if (n > 0 || OFF || del || o != 0)
                    {
                        //save it
                        int amt = 1; //this is number of notes to edit
                        if (o != 0)
                            amt = vertCells;

                        if (selcol < 27) //fm
                        {
                            for (int cur = 0; cur < amt; cur++)
                            {
                                FMNote note = fmMeasures[selcol / 3][songLayout[selectedMeasure][selcol / 3]][selrow + cur];
                                if (o == 0 || (o == -1 && note.note >= 13) || (o == 1 && note.note != 0 && note.note < 8*12))
                                {
                                    fmMeasures[selcol / 3][songLayout[selectedMeasure][selcol / 3]][selrow + cur] = new FMNote { note = del?0:(o == 0 ? (n + (OFF ? 256 : 0)) : note.note + 12 * o), inst = (note.vol == -1 ? instrument : note.inst), vol = del ? -1 : (note.vol == -1 ? volume : note.vol) };
                                    instrument = fmMeasures[selcol / 3][songLayout[selectedMeasure][selcol / 3]][selrow + cur].inst;
                                    if (fmMeasures[selcol / 3][songLayout[selectedMeasure][selcol / 3]][selrow + cur].vol != -1)
                                        volume = fmMeasures[selcol / 3][songLayout[selectedMeasure][selcol / 3]][selrow + cur].vol;
                                }
                            }
                        }
                        else if (selcol == 34) //noise
                        {
                            NoiseNote note = noiseMeasures[songLayout[selectedMeasure][13]][selrow];
                            noiseMeasures[songLayout[selectedMeasure][13]][selrow] = new NoiseNote { periodic = (n % 2) == 0, vol = del ? -1 : (note.vol == -1 ? volume : note.vol) };
                            if (noiseMeasures[songLayout[selectedMeasure][13]][selrow].vol != -1)
                                volume = noiseMeasures[songLayout[selectedMeasure][13]][selrow].vol;
                        }
                        else if (selcol > 27) //psg
                        {
                            for (int cur = 0; cur < amt; cur++)
                            {
                                PSGNote note = psgMeasures[(selcol - 28) / 2][songLayout[selectedMeasure][(selcol - 28) / 2 + 10]][selrow+cur];
                                if (o == 0 || (o == -1 && note.note >= 13) || (o == 1 && note.note != 0 && note.note < 8 * 12))
                                {
                                    psgMeasures[(selcol - 28) / 2][songLayout[selectedMeasure][(selcol - 28) / 2 + 10]][selrow + cur] = new PSGNote { note = (o == 0 ? n : (note.note + 12 * o)), vol = del ? -1 : (note.vol == -1 ? volume : note.vol) };
                                    if (psgMeasures[(selcol - 28) / 2][songLayout[selectedMeasure][(selcol - 28) / 2 + 10]][selrow + cur].vol != -1)
                                        volume = psgMeasures[(selcol - 28) / 2][songLayout[selectedMeasure][(selcol - 28) / 2 + 10]][selrow + cur].vol;
                                }
                            }
                        }
                        if (o == 0)
                        {
                            selectedNoteCell += CELLSPERROW * (vertCells + advance - 1);
                            vertCells = 1;
                        }
                    }
                }
                else if ((selcol < (songLayout[selectedMeasure][PERCBOOL] == 1 ? 18 : 27) && (selcol % 3 == 1)) || (selcol > 27 && (selcol % 2 == 1)))
                {
                    int v = -2;
                    //number edit values
                    switch (k)
                    {
                        case Keys.Delete:
                        case Keys.Back:
                            v = -1;
                            break;
                        case Keys.D0:
                            v = 0;
                            break;
                        case Keys.D1:
                            v = 1;
                            break;
                        case Keys.D2:
                            v = 2;
                            break;
                        case Keys.D3:
                            v = 3;
                            break;
                        case Keys.D4:
                            v = 4;
                            break;
                        case Keys.D5:
                            v = 5;
                            break;
                        case Keys.D6:
                            v = 6;
                            break;
                        case Keys.D7:
                            v = 7;
                            break;
                        case Keys.D8:
                            v = 8;
                            break;
                        case Keys.D9:
                            v = 9;
                            break;
                        case Keys.A:
                            v = 10;
                            break;
                        case Keys.B:
                            v = 11;
                            break;
                        case Keys.C:
                            v = 12;
                            break;
                        case Keys.D:
                            v = 13;
                            break;
                        case Keys.E:
                            v = 14;
                            break;
                        case Keys.F:
                        case Keys.Tab:
                            v = 15;
                            break;
                    }
                    if (v == -1)
                    {
                        //save a fresh blank cell
                        if (selcol < 27) //fm
                            fmMeasures[selcol / 3][songLayout[selectedMeasure][selcol / 3]][selrow] = new FMNote { note = 0, inst = 0, vol = -1 };
                        else if (selcol == 35) //noise
                            noiseMeasures[songLayout[selectedMeasure][13]][selrow] = new NoiseNote { periodic = false, vol = -1 };
                        else if (selcol > 27) //psg
                            psgMeasures[(selcol - 28) / 2][songLayout[selectedMeasure][(selcol - 28) / 2 + 10]][selrow] = new PSGNote { note = 0, vol = -1 };
                        selectedNoteCell += CELLSPERROW * advance;
                    }
                    else if (v > -2)
                    {
                        //save it
                        if (selcol < 27) //fm
                            fmMeasures[selcol / 3][songLayout[selectedMeasure][selcol / 3]][selrow].vol = v;
                        else if (selcol == 35) //noise
                            noiseMeasures[songLayout[selectedMeasure][13]][selrow].vol = v;
                        else if (selcol > 27) //psg
                            psgMeasures[(selcol - 28) / 2][songLayout[selectedMeasure][(selcol - 28) / 2 + 10]][selrow].vol = v;
                        volume = v;
                        selectedNoteCell += CELLSPERROW * advance;
                    }
                }
                else if (selcol < 27 && (selcol % 3 == 2))
                {
                    int v = -1;
                    //instruments
                    switch (k)
                    {
                        case Keys.D0:
                            v = 0;
                            break;
                        case Keys.D1:
                            v = 1;
                            break;
                        case Keys.D2:
                            v = 2;
                            break;
                        case Keys.D3:
                            v = 3;
                            break;
                        case Keys.D4:
                            v = 4;
                            break;
                        case Keys.D5:
                            v = 5;
                            break;
                        case Keys.D6:
                            v = 6;
                            break;
                        case Keys.D7:
                            v = 7;
                            break;
                        case Keys.D8:
                            v = 8;
                            break;
                        case Keys.D9:
                            v = 9;
                            break;
                        case Keys.A:
                            v = 10;
                            break;
                        case Keys.B:
                            v = 11;
                            break;
                        case Keys.C:
                            v = 12;
                            break;
                        case Keys.D:
                            v = 13;
                            break;
                        case Keys.E:
                            v = 14;
                            break;
                        case Keys.F:
                            v = 15;
                            break;
                    }
                    //save it
                    if (v > -1)
                    {
                        fmMeasures[selcol / 3][songLayout[selectedMeasure][selcol / 3]][selrow].inst = v;
                        instrument = v;
                        selectedNoteCell += CELLSPERROW * advance;
                    }
                }
                else if (selcol == 36)
                {
                    //noise rate!
                    int v = -1;
                    switch (k)
                    {
                        case Keys.D0:
                            v = 0;
                            break;
                        case Keys.D1:
                            v = 1;
                            break;
                        case Keys.D2:
                            v = 2;
                            break;
                        case Keys.D3:
                            v = 3;
                            break;
                    }
                    if (v > -1)
                        noiseMeasures[songLayout[selectedMeasure][13]][selrow].rate = v;
                }
                else if (songLayout[selectedMeasure][PERCBOOL] == 1 && selcol > 17 && selcol < 27)
                {
                    //editing a perc note!
                    if (k == Keys.Tab)
                    {
                        //toggle!
                        PercNote note = percussionMeasures[songLayout[selectedMeasure][PERCCELL]][selrow];
                        switch (selcol)
                        {
                            case 18:
                                note.bass = !note.bass;
                                break;
                            case 19:
                                note.snare = !note.snare;
                                break;
                            case 21:
                                note.tom = !note.tom;
                                break;
                            case 22:
                                note.cymbal = !note.cymbal;
                                break;
                            case 24:
                                note.hihat = !note.hihat;
                                break;
                        }
                        selectedNoteCell += CELLSPERROW * advance;
                    }

                }
            }
            e.Handled = true;
        }

        string getNoteName(int note)
        {
            if (note == 0)
                return "--";
            note -= 1;
            int note2 = note % 12;
            return NoteNames[note2] + (note/12+1);
        }

        void addFMMeasure(int w)
        {
            List<FMNote> t = new List<FMNote>();
            for (int i = 0; i < 32; i++)
                t.Add(new FMNote { note = 0, inst = 0, vol = -1 });
            fmMeasures[w].Add(t);
        }

        void addPSGMeasure(int w)
        {
            List<PSGNote> t = new List<PSGNote>();
            for (int i = 0; i < 32; i++)
                t.Add(new PSGNote { note = 0, vol = -1 });
            psgMeasures[w].Add(t);
        }

        void addNoiseMeasure()
        {
            List<NoiseNote> t = new List<NoiseNote>();
            for (int i = 0; i < 32; i++)
                t.Add(new NoiseNote { periodic = false, rate = 0, vol = -1 });
            noiseMeasures.Add(t);
        }

        void addCommandMeasure()
        {
            List<CommandNote> t = new List<CommandNote>();
            for (int i = 0; i < 32; i++)
                t.Add(new CommandNote
                {
                    instrumentswap = false,
                    instrument = 0,
                    jump = false,
                    jumptargetmeasure = 0,
                    jumptargetnote = 0,
                    percvolchange = false,
                    percVol = new byte[] { 0, 0, 0, 0, 0 },
                    detune = false,
                    detuneAMTS = new byte[] { 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 },
                    vsEnabled = new byte[] { 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 },
                    vsAMTS = new byte[] { 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 },
                    vsTIMES = new byte[] { 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 }
                });
            commandMeasures.Add(t);
        }

        void addPercussionMeasure()
        {
            List<PercNote> t = new List<PercNote>();
            for (int i = 0; i < 32; i++)
                t.Add(new PercNote { bass = false, cymbal = false, hihat = false, snare = false, tom = false });
            percussionMeasures.Add(t);
        }

        void addInstrument()
        {
            Instrument t = new Instrument();
            for(int i = 0; i < 2; i++)
            {
                t.waves[i].attack = 0;
                t.waves[i].decay = 0;
                t.waves[i].sustain = 0;
                t.waves[i].release = 0;
                t.waves[i].ampmod = false;
                t.waves[i].half = false;
                t.waves[i].ksl = 0;
                t.waves[i].ksr = false;
                t.waves[i].multi = 0;
                t.waves[i].sustone = false;
                t.waves[i].vibrato = false;
            }
            t.attenuation = 0;
            t.feedback = 0;
            t.name = "inst_" + instruments.Count.ToString();
            instruments.Add(t);

        }

    }
}
