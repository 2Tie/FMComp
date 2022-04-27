resetMusicEngine:
    xor a
    ld (rtrackpointerl), a
    ld (rtrackpointerh), a
    ld (rdelaycounter), a
    ld (rtrackprogressl), a
    ld (rtrackprogressh), a
    ld (rinstrumentpointerl), a
    ld (rinstrumentpointerh), a
    ret
    
loadSong:
    ;hl is loaded with a pointer to the song data
    call resetMusicEngine
    ;first, read the chipflags
    ld a, (hl)
    ld (rtrackchips), a
    inc hl
    ;then the song name
    ld b, 0
    ld c, 0
-:
    ld a, (hl)
    push hl
    ld hl, rtrackname
    add hl, bc
    ld (hl), a
    pop hl
    inc hl
    ld a, c
    add a, 1
    ld c, a
    cp 32
    jr nz, -
    ;next the author
    ld c, 0
-:
    ld a, (hl)
    push hl
    ld hl, rtrackauth
    add hl, bc
    ld (hl), a
    pop hl
    inc hl
    ld a, c
    add a, 1
    ld c, a
    cp 16
    jr nz, -
    ;the number of instruments
    ld a, (hl)
    inc hl
    ;save the pointer
    ld (rinstrumentpointer), hl
    ;now advance past the instruments
    sla a
    sla a
    sla a ;multiply count by instrument byte size (eight)
    add a, l
    jp nc, +
    inc h
+:  ld l, a
    ld (rtrackpointer), hl ;save the start of the track!
    ret


updateMusic:
    ;check the vibrato slide registers
    ld bc, 0
    ld hl, rvstimes
_rvsloop:
    ld a, (hl)
    or a
    jp z, _rvsloopend
    ;if nonzero, there's a thing to do!!
    ;load the amount, check what type
    ld hl, rvsamts
    add hl, bc
    add hl, bc
    ld a, (hl)
    and $80
    jp nz, _vibrato
    ;for slide, add to accumulator, add to register, decrement timer
    ld d, (hl)
    inc hl
    ld a, (hl)
    add a, d ;added
    ld d, a ;backup the sum (signed)

    and $01
    ld (hl), a ;reload vs accumulator
    sla d;
    sra d;
    sra d;
    ld hl, rnotes
    add hl, bc
    add hl, bc
    ld a, (hl) ;load last note
    add a, d ;add new value (signed)
    ld (hl), a ;and store it for later
    call umupdatenotelo
    ;check for over/underflow!
    call umcheckflow
    ;update timer
    ld hl, rvstimes
    add hl, bc
    ld a, (hl)
    sub 1
    ld (hl), a
    jp _rvsloopend

    ;for vibrato, add to accumulator, add to register,
_vibrato:
    ;mask off bitflag
    ld a, (hl)
    and $7F
    ld d, a ;back it up
    inc hl
    ld a, (hl) ;load timer
    or a
    jp m, _neg ;check sign
    ;if positive (below 80), add sum value and decrement timer
    ;in a, ($F1)
    ld hl, rnotes
    add hl, bc
    add hl, bc
    ld a, (hl) ;saved note loaded into a
    add a, d
    ld (hl), a ;update saved note
    call umupdatenotelo
    call umcheckflow
    ;update timer
    ld hl, rvsamts
    add hl, bc
    add hl, bc
    inc hl ;load up timer again
    ld a, (hl)
    sub 1 ;this sets carry, while dec doesn't
    ld (hl), a
    jp _vibend
_neg:
    ;if negative (above 80), make value negative, then add value and increment timer
    ld a, d
    neg
    ld d, a
    ld hl, rnotes
    add hl, bc
    add hl, bc
    ld a, (hl) ;saved note loaded into a
    add a, d
    ld (hl), a ;update saved note
    call umupdatenotelo
    call umcheckflow
    ;update timers
    ld hl, rvsamts
    add hl, bc
    add hl, bc 
    inc hl ;load up timer again
    ld a, (hl)
    add a, 1
    ld (hl), a
_vibend
    ;if timer is now zero, reload accum with positive (carry set) or 
    ;negative (carry not set) length register*2
    jp nz, _rvsloopend
    jp c, _vibreloadpos
    ld hl, rvstimes
    add hl, bc
    ld a, (hl)
    add a, a ;doubled
    cpl
    inc a ;negative
    ld hl, rvsamts
    add hl, bc
    add hl, bc
    inc hl
    ld (hl), a
    jp _rvsloopend
_vibreloadpos
    ld hl, rvstimes
    add hl, bc
    ld a, (hl)
    add a, a ;doubled
    ld hl, rvsamts
    add hl, bc
    add hl, bc
    inc hl
    ld (hl), a
    ;fall into
_rvsloopend:
    inc c
    ld a, 12 ;only through the 12 channels
    cp c
    jp z, _rvsloopexit
    ld hl, rvstimes
    add hl, bc
    jp _rvsloop
_rvsloopexit:
    ;check the wait counter - if zero, process next command
    ld a, (rdelaycounter)
    or 0
    jp nz, ++
umread:
    ;counter's at zero, time to read next com
    call readNextTrackByte
    ld b, a ;backup the byte read
    ;store delay into rdelay counter
    and $0F
    ld (rdelaycounter), a
    ;read the code
    ld a, b
    and $f0 ;mask the code bits
    ;time for jump table?
    srl a
    srl a
    srl a
    srl a
    ld l, a
    ld h, $00
    add hl, hl
    ld bc, umjumptable
    add hl, bc
    ld a, (hl)
    inc hl
    ld h, (hl)
    ld l, a
    jp (hl)
    
umupdatenotelo:
    push af
    ;now check if this is FM or PSG
    ld a, $08
    sub c
    jr c, + ;jump if psg
    ;save FM note
    ld a, $10
    add a, c
    out ($F0), a ;latch register to low val
    ld a, (hl)
    out ($F1), a ;save new value
    pop af
    ret
+:
    ;save psg note?
    ;reconstruct note command.
    ld a, c
    sub 9
    sla a
    sla a
    sla a
    sla a
    sla a ;shift left 5
    or $80
    ld b, a
    ld a, (hl)
    and $0F ;mask off overflow?
    ld (hl), a
    or b
    out ($7F), a
    ld b, 0
    pop af
    ret

umcheckflow:
    ;we can check the sign bit without affecting carry flag
    ;check if psg or not to see which bit to check:
    bit 7, d
    jr nz, _sumneg
    ;positive, an addition. carry means increment byte
    push af
    ;if psg, shift left four to get new carry flag
    ld a, 8
    sub c
    jp c, _psgshift ;if psg, jump
    pop af
    jr c, +
    ret
_psgshift:
    pop af
    sla a
    sla a
    sla a
    sla a
    jr c, +
    ret
+:
    inc hl  ;get to hi byte
    ld d, (hl)
    ld a, $08 ;check if psg
    sub c
    jr c, + ;jump if psg
    ld a, $01
    add a, d
    ld (hl), a ;increment the blocknum

    ld a, $20
    add a, c
    out ($F0), a ;latch register to high val
    ld a, (hl)
    out ($F1), a ;write new val
    ret
+:
    ld d, (hl)
    inc d
    ld (hl), d ;increment the high bits
    ld a, d
    and $3F
    out ($7F), a ;write new val (already latched)
    ret                         

_sumneg
    ;negative, a subtraction. no carry means decrement byte 
    push af
    ;if psg, shift left four to get new carry flag
    ld a, 8
    sub c
    jp c, _negpsgshift ;if psg, jump
    pop af
    jr nc, +
    ret
_negpsgshift:
    pop af
    sla a
    sla a
    sla a
    sla a
    jr c, +
    ret
+:
    inc hl
    ld a, $08
    sub c
    jr c, + ;jump if psg
    ld a, (hl)
    ld d, $01
    sub d
    ld (hl), a ;decrement the blocknum
    
    ld a, $20
    add a, c
    out ($F0), a ;latch register to high val
    ld a, (hl)
    out ($F1), a ;write new val
    ret
+:
    ld d, (hl)
    dec d
    ld (hl), d
    ld a, d
    out ($7F), a
    ret

umjumptable:
    .dw umloadpsgfull, umloadpsgnote, umloadfmfull, umloadfmnote
    .dw umloadpsgvol, umjumpaddress, umloadfmvol, umloadinstrument
    .dw umenablepercussion, umplaypercussion, umpercussionvol, umdisablepercussion
    .dw umlongwait, umvibslide, error, error
    ;last two are placeholder, will hang but preserve track position in ram


umvibslide:
    ;first byte is channel
    call readNextTrackByte
    ld hl, rvstimes
    ld c, a ;bc will have the reg
    ld b, 0
    add hl, bc ;hl is time
    call readNextTrackByte
    ld (hl), a ;stored
    sla c ;turn reg into word offset
    ld hl, rvsamts
    add hl, bc ;hl is now amt
    call readNextTrackByte
    ld (hl), a ;load it up
    jp +
umenablepercussion:
    ;gotta set up the registers right...
    ld a, $26
    out ($f0), a
    ld a, $05
    out ($f1), a
    ld a, $27
    out ($f0), a
    ld a, $05
    out ($f1), a
    ld a, $28
    out ($f0), a
    ld a, $01
    out ($f1), a
    ld a, $16
    out ($f0), a
    ld a, $20
    out ($f1), a
    ld a, $17
    out ($f0), a
    ld a, $50
    out ($f1), a
    ld a, $18
    out ($f0), a
    ld a, $c0
    out ($f1), a
    ld a, $0e
    out ($f0), a
    ld a, $10
    out ($f1), a
    jp +
umplaypercussion:
    ld a, $0e
    out ($f0), a
    call readNextTrackByte
    ;set rhythm enable for sanity
    or $20
    out ($f1), a
    jp +
umpercussionvol:
    ld a, $36
    out ($f0), a
    call readNextTrackByte
    and $0f ;mask off, just in case
    out ($f1), a
    ld a, $37
    out ($f0), a
    call readNextTrackByte
    out ($f1), a
    ld a, $38
    out ($f0), a
    call readNextTrackByte
    out ($f1), a
    jp +
umdisablepercussion:
    ld a, $0e
    out ($f0), a
    xor a
    out ($f1), a
    jp +
umlongwait:
    call readNextTrackByte
    ld (rdelaycounter), a
    jp +
umloadinstrument:
    ;load instrument data into $00 and $01, $02 and $03, $04 and $05, $06 and $07
    ;next byte is instrument number
    call readNextTrackByte
    ;multiply by eight to get instrument address offset
    sla a
    sla a
    sla a
    ld c, a
    xor a
    ld b, a
    ld hl, (rinstrumentpointer)
    add hl, bc
    ;now load up the data
    ld a, $00
    out ($f0), a
    ld a, (hl)
    inc hl
    out ($f1), a
    ld a, $01
    out ($f0), a
    ld a, (hl)
    inc hl
    out ($f1), a
    ld a, $02
    out ($f0), a
    ld a, (hl)
    inc hl
    out ($f1), a
    ld a, $03
    out ($f0), a
    ld a, (hl)
    inc hl
    out ($f1), a
    ld a, $04
    out ($f0), a
    ld a, (hl)
    inc hl
    out ($f1), a
    ld a, $05
    out ($f0), a
    ld a, (hl)
    inc hl
    out ($f1), a
    ld a, $06
    out ($f0), a
    ld a, (hl)
    inc hl
    out ($f1), a
    ld a, $07
    out ($f0), a
    ld a, (hl)
    out ($f1), a
    jp +
umjumpaddress:
    call readNextTrackByte
    ld b, a
    call readNextTrackByte
    ld (rtrackprogressh), a
    ld a, b
    ld (rtrackprogress), a
    jp +
umloadpsgfull:
    call readNextTrackByte ;vol first??
    out ($7f), a
umloadpsgnote:
    call readNextTrackByte  ;low note
    out ($7f), a
    ;grab what register it should be
    ld c, a
    ld b, a ;backup for saving the data
    srl c
    srl c
    srl c
    srl c
    srl c ;>>5
    ld a, 3
    and c
    ld c, a ;c is now the register
    ld a, 9
    add a, c
    ld c, a
    ld a, b ;restore the data value
    ld b, 0 ;fix the bc count
    ld hl, rnotes
    add hl, bc
    add hl, bc
    and $0F ;mask to relevant bits
    ld (hl), a ;save it

    call readNextTrackByte  ;hi note
    out ($7f), a
    inc hl
    and $3F ;mask to relevant bits
    ld (hl), a ;save the hi note
    jp +
umloadpsgvol:
    call readNextTrackByte
    out ($7f), a
    jp +
umloadfmfull:
    ;first byte is track number, second is intrument/vol, 3+4 is note
    call readNextTrackByte
    ld b, a
    call readNextTrackByte
    ld c, a
    call readNextTrackByte
    ld d, a
    call readNextTrackByte
    ld e, a
    ld a, b ;track number, 0-9
    or $30
    out ($f0), a
    ld a, c ;instrument + vol
    out ($f1), a
-:  ld a, b
    or $10
    out ($f0), a
    ld a, d ;low freq byte
    out ($f1), a
    ld a, b
    or $20
    out ($f0), a
    ld a, e ;high freq byte + key down
    out ($f1), a
    ;now save the note
    ld hl, rnotes
    ld c, b
    ld b, 0
    add hl, bc
    add hl, bc
    ld (hl), d ;(low first)
    inc hl
    ld (hl), e ;(then hi)
    jp +
umloadfmnote:
    call readNextTrackByte
    ld b, a
    call readNextTrackByte
    ld d, a
    call readNextTrackByte
    ld e, a
    jp -
umloadfmvol:
    call readNextTrackByte
    ld b, a
    call readNextTrackByte
    ld c, a
    ld a, b
    and $30
    out ($f0), a
    ld a, c
    out ($f1), a
    ;fallthrough to + here
+:  ld a, (rdelaycounter)
    or 0
    jp z, umread
++:  dec a
    ld (rdelaycounter), a
    ret
    
readNextTrackByte:
    ;returns the next byte of track data in register a
    push hl
    push bc
    ld hl, (rtrackpointer)
    ld b, h
    ld c, l
    ld hl, (rtrackprogress)
    add hl, bc
    ld a, (hl)
    ;increment progress before returning
    ld hl, (rtrackprogress)
    inc hl
    ld (rtrackprogress), hl
    pop bc
    pop hl
    ret

.struct vsaccum
amt db
accum db
.endst

.enum $d000 ;start of sound values
rtrackname ds 32
rtrackauth ds 16
rtrackchips db
rtrackpointer .dw
rtrackpointerl db
rtrackpointerh db
rdelaycounter db
rtrackprogress .dw
rtrackprogressl db
rtrackprogressh db
rinstrumentpointer .dw
rinstrumentpointerl db
rinstrumentpointerh db
rvstimes ds 12
rvsamts instanceof vsaccum 12 ;amount followed by accumulator
rnotes ds 24 ;high and low bytes
.ende