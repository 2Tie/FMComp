layout editor uses + and - keys to choose pattern. arrow keys or click to change what cell is selected. the + and - to the left of the measure number can be clicked to insert a new measure after the current one, or delete the current measure respectively. mouse wheel or arrow keys can scroll the view.

left click will select or edit some options in the top left panel. clicking on the FPN text itself switches to BPM, and vice versa. clicking on the song name or author name will let you edit them, signified by a > before the text. any english alphanumeric should work, along with space and backspace. the X's can be enabled with a click. the copy and paste buttons do as advertised, copying a group of selected notes and pasting them in compatible cells.

leftmost cell on a note is always the pitch. middle is volume (0 is max). FM has a third cell for instrument. custom instrument (#0) currently does nothing. tab on an FM pitch will turn the note off, for PSG you have to set volume to F to turn them off. FM notes need to be turned off before a new FM note in the same channel will have attack. the last volume and instrument entered are remembered for new notes. Instrument 0 utilizes the currently set custom instrument (which needs to be set via command note). you can also increase or decrease the octave of selected notes with page up or page down.

tab in the pattern editor (a cell must be selected, not a row) will toggle percussion for that row. percussion notes are toggled with tab in the note editor. similar to FM notes, percussion needs untoggled notes between new notes, though there is a checkbox in the song info region (top left) that will automatically insert off-notes between each percussion note if needed.

the black vertical column is for Command notes, editable in the top right panel. Jump will immediately jump to the specified measure (click to enable it, then enter a number in the box). Percussion volume changes the volume of each percussion instrument (bass, snare, tom, cymbal, hihat). instrument change loads a different custom instrument entry into ID 0. Detune offsets the raw note value for the specified channels until the next detune command, amount increased or decreased with mouse click. commands are always executed before any notes on the same row, except for jump, which happens just before the next row would happen.

the FM instruments are mapped as following:
0: custom     8: organ
1: violin     9: horn
2: guitar     A: synth
3: piano      B: harpsichord
4: flute      C: vibraphone
5: clarinet   D: synth bass
6: oboe       E: acoustic bass
7: trumpet    F: electric guitar
in order to play a custom instrument, you must first load one with the Inst Change command. (no instrument is loaded by default!)

The panel below the custom instrument list is for editing the selected custom instrument. each operator for the instrument has its own section. Attack must be nonzero on the Carrier wave for any tone to be generated, and the Modulation wave will only be applied if its attack is nonzero (optional). Multiplier increases the frequency. Level Scale decreases the volume of higher pitches. Scale Rate accelerates the ADSR envelope. AM is amplitude modulation, AKA tremolo. Further detail can be found in the YM2413 Application Manual.

though i'd like to make another program specifically geared for making custom instruments, a great tool to play with and freely edit custom instruments in realtime can be found at https://www.smspower.org/Homebrew/SegaMasterSystemYM2413FMTool-SMS

the Save and Load buttons in the song info panel will do as advertised, saving and loading the project file. Export will turn the song into either raw song data for inclusion in source code, or directly into a rom that will play the song and no more. the audio driver source for playing the track data is included (fmdriver.asm), with instructions in driver.txt.

exported roms are currently confirmed to work in Emulicious (recommended), Kega Fusion, and MEKA.

if you're not getting any sounds from the FM channels, make sure the instrument (rightmost part of the note) is nonzero or that you've edited a custom instrument and have assigned it via command cell!