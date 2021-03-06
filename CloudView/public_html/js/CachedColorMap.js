
(function(JACERE) {
	
	JACERE.CachedColorMap = function(ramp, stretch, desiredDestinationBins)
	{
		this.ramp = ramp;
		this.map = new JACERE.IntervalMap(stretch, desiredDestinationBins, false);
		this.length = this.map.binCount;

		// allow overflow
		this.bins = JACERE.Util.createZeroArray(this.map.binCount + 1);

		var intervals = this.map.getIntervals();
		for (var i = 0; i < intervals.length; i++) {
			var interval = intervals[i];
			this.bins[interval.index] = this.ramp.getColor(interval.stretchRatio);
		}

		// overflow
		this.bins[this.bins.length - 1] = this.bins[this.bins.length - 2];
	};

	JACERE.CachedColorMap.prototype = {

		constructor: JACERE.CachedColorMap,
		
		getColor: function(value) {
			var index = ~~this.map.getInterval(value);
			return this.bins[index];
		}
		
	};
	
}(self.JACERE = self.JACERE || {}));