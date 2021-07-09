/world
	var/list/contents = list()

	var/log = null

	var/area = /area
	var/turf = /turf
	var/mob = /mob

	var/name = "OpenDream World"
	var/time
	var/timeofday
	var/realtime
	var/tick_lag = 1
	var/cpu = 0
	var/fps = null
	var/tick_usage
	var/sleep_offline = 0

	var/maxx = 0
	var/maxy = 0
	var/maxz = 0
	var/icon_size = 32
	var/view = 5

	var/address
	var/port
	var/url
	var/status
	var/list/params = null
	
	var/system_type

	proc/New()
	proc/Del()

	proc/Reboot()
		CRASH("/world.Reboot() is not implemented")
		
	proc/Repop()
		CRASH("/world.Repop() will not be implemented")
